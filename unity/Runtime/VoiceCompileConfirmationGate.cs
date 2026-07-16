using UnityEngine;

namespace DreamCodeVRPlus
{
    /// <summary>
    /// PURE, engine-independent confirmation state machine for a Mode-A runtime C#
    /// compile (Phase 7). No UnityEngine dependency, so it is deterministically
    /// EditMode-testable without a device. Lifecycle: Idle -> (Submit) -> Pending ->
    /// (Confirm) -> Idle, with (ExpireIfStale) and (Reset) as escape hatches.
    ///
    /// SECURITY INTENT: today the networked demo compiles ANY NID-94 {type:"code"}
    /// body with no human-in-the-loop (a remote-code path). This gate lets a client
    /// require an explicit user confirmation before the compile, so a spoofed/socially
    /// engineered "run this" cannot silently execute. It only DEFERS the compile; it
    /// never runs code itself.
    /// </summary>
    public sealed class CompileConfirmationState
    {
        public enum Phase
        {
            Idle,
            Pending,
        }

        private string _pendingCode;
        private long _pendingSinceMs;
        private readonly long _ttlMs;

        /// <param name="ttlMs">How long a pending compile waits for confirmation before
        /// it is dropped (fail-closed). Default 30 s.</param>
        public CompileConfirmationState(long ttlMs = 30000)
        {
            _ttlMs = ttlMs;
            Current = Phase.Idle;
        }

        public Phase Current { get; private set; }

        public bool HasPending => Current == Phase.Pending;

        /// <summary>The code awaiting confirmation (null when idle). For preview only.</summary>
        public string PendingCode => _pendingCode;

        /// <summary>
        /// Stash incoming code and await confirmation. A newer submission REPLACES an
        /// older pending one (the user only ever confirms the latest request).
        /// </summary>
        public void Submit(string code, long nowMs)
        {
            _pendingCode = code;
            _pendingSinceMs = nowMs;
            Current = Phase.Pending;
        }

        /// <summary>
        /// The user confirmed: return the code to compile and return to Idle. Returns
        /// null when nothing is pending (nothing to run — fail-closed).
        /// </summary>
        public string Confirm()
        {
            if (Current != Phase.Pending)
            {
                return null;
            }

            string code = _pendingCode;
            Reset();
            return code;
        }

        /// <summary>
        /// Drop stale pending code that was never confirmed. Returns true if it expired.
        /// </summary>
        public bool ExpireIfStale(long nowMs)
        {
            if (Current == Phase.Pending && nowMs - _pendingSinceMs >= _ttlMs)
            {
                Reset();
                return true;
            }

            return false;
        }

        /// <summary>Clear any pending code (call on reset / disconnect / scene change).</summary>
        public void Reset()
        {
            _pendingCode = null;
            _pendingSinceMs = 0;
            Current = Phase.Idle;
        }
    }

    /// <summary>
    /// Opt-in MonoBehaviour wrapper around <see cref="CompileConfirmationState"/>.
    ///
    /// DEFAULT OFF: <see cref="requireConfirmation"/> is false, so a host that does not
    /// wire it in, or leaves it disabled, behaves EXACTLY as today (byte-identical
    /// legacy demo). When armed, a host routes an incoming compile through
    /// <see cref="SubmitOrPassthrough"/>: if confirmation is required the code is stashed
    /// (returns false = "do not compile yet") and the host shows a prompt; on the user's
    /// explicit "yes" the host calls <see cref="Confirm"/> and compiles the returned code.
    ///
    /// ON-DEVICE PENDING: the actual compile (RuntimeCSharpCompiler) and voice/'yes'
    /// capture are Mono-only and require a Quest; this component makes NO runtime claim.
    /// The state machine above is verified in EditMode only.
    /// </summary>
    public sealed class VoiceCompileConfirmationGate : MonoBehaviour
    {
        [Tooltip("Require an explicit user confirmation before a Mode-A compile. " +
                 "DEFAULT OFF = today's behaviour (compile immediately).")]
        public bool requireConfirmation = false;

        [Tooltip("Seconds a pending compile waits for confirmation before it is dropped.")]
        public float pendingTtlSeconds = 30f;

        private CompileConfirmationState _state;

        public bool HasPending => _state != null && _state.HasPending;
        public string PendingCode => _state?.PendingCode;

        private void Awake()
        {
            _state = new CompileConfirmationState((long)(pendingTtlSeconds * 1000f));
        }

        /// <summary>
        /// Host entry point for an incoming compile. Returns true when the caller MAY
        /// compile `code` immediately (confirmation not required); false when the code
        /// has been stashed and a confirmation prompt should be shown instead.
        /// </summary>
        public bool SubmitOrPassthrough(string code, long nowMs)
        {
            if (!requireConfirmation)
            {
                return true; // legacy passthrough: compile now
            }

            _state.Submit(code, nowMs);
            return false; // deferred: await Confirm()
        }

        /// <summary>Return the confirmed code to compile (null if none), and clear.</summary>
        public string Confirm()
        {
            return _state?.Confirm();
        }

        /// <summary>Call periodically (e.g. from Update) to drop stale, unconfirmed code.</summary>
        public bool ExpireIfStale(long nowMs)
        {
            return _state != null && _state.ExpireIfStale(nowMs);
        }

        /// <summary>Clear any pending code (call on reset / disconnect).</summary>
        public void ResetPending()
        {
            _state?.Reset();
        }
    }
}
