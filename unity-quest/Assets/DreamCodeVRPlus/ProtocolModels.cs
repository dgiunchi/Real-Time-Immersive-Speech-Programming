namespace DreamCodeVRPlus
{
    /// <summary>
    /// Mirrors <c>dcvr-behaviour-dsl/src/bounds.rs</c>. The Rust backend is the
    /// authoritative validator; these values let the Unity client re-clamp
    /// defensively.
    ///
    /// KEEP IN SYNC with the Rust crate. `tests/tests/bounds_parity.rs` parses this
    /// file and fails the build on any divergence — which is how the numbers below were
    /// caught after the plan vocabulary grew: the Rust budgets were raised so that a
    /// scene could actually be described, and this file was not, so the client's
    /// defence-in-depth re-check would have rejected plans the server had approved.
    /// Defence in depth that disagrees with the layer above it is just a bug.
    /// </summary>
    public static class ProtocolModels
    {
        public const string SupportedSchemaVersion = "1.0";

        public const int MaxActions = 48;
        public const int MaxSpawnCount = 32;
        public const int MaxTotalSpawnedPerSession = 512;
        public const int MaxHierarchyDepth = 3;

        public const float ScaleMin = 0.1f;
        public const float ScaleMax = 5.0f;
        public const float MoveSpeedMax = 5.0f;
        public const float MoveAmplitudeMax = 5.0f;
        // SP-04 / PE-03 (unbounded-drift live bug): a move=once translates forever.
        // Cap the CUMULATIVE travelled distance for a one-shot move and STOP there.
        // PARITY-CHECKED by Rust bounds_parity.rs against
        // dcvr-behaviour-dsl/src/bounds.rs MOVE_MAX_TOTAL_DISTANCE (= 10.0).
        // Exact name + value MUST stay in sync.
        public const float MoveMaxTotalDistance = 10.0f;
        public const float RotateDegPerSecMax = 360.0f;
        public const float PhysicsMassMin = 0.1f;
        public const float PhysicsMassMax = 100.0f;

        // --- Perceptual / user-frame safety bounds (TRACK U1) ---
        // Personal-space sphere around the headset. Nothing generated may spawn into
        // it, and a continuously-moving object that would enter it is clamped/stopped
        // (user-frame invariant; blocks "object spawned inside your head" body-horror).
        public const float PersonalSpaceRadius = 0.5f;
        // PE-01 (full-FOV blackout): refuse a single object whose angular size at the
        // camera would cover more than this fraction of the forward view (~80%).
        public const float MaxForwardCoverageFraction = 0.80f;
        // Approx human horizontal FOV used for the angular-size coverage estimate.
        public const float AssumedHorizontalFovDeg = 90.0f;
        // SP-03 (disguise / false-affordance via co-location): two generated objects
        // are treated as overlapping/occluding when their centres are within this
        // fraction of the smaller object's bounding radius.
        public const float OverlapCentreFraction = 0.5f;

        // --- Perceptual / embodied DETECTION thresholds (TRACK U2) ---
        // DISCLOSURE / MONITORING thresholds. These NEVER block free creation:
        // when armed (comfortIntensity) and exceeded they raise a transparency
        // disclosure (PerceptualDisclosure) — creative builds are never restricted.
        // PARITY-CHECKED against dcvr-behaviour-dsl/src/bounds.rs.
        //
        // Cumulative real-world head displacement (m) during an active content-motion
        // plan that is flagged as possible herding (Ishii 2016; Casey 2021). Monitor-only.
        public const float UserDriftDisclosureThresholdM = 1.0f;
        // Fraction of generated objects translating coherently in one direction within
        // a plan before it is flagged as a vection/world-drift pattern (Casey 2021;
        // Ishii 2016). Monitor-only.
        public const float VectionCoherentFlowBudget = 0.75f;
        // AdCube-style confinement radius (m) from the generated root; content staged
        // beyond it (blind-spot placement) is disclosed (Lee 2021). Monitor-only.
        public const float ConfinementRegionRadiusM = 8.0f;
        // Fujita 2023 shielding cue: a generated occluder whose depth differs from the
        // object it occludes by more than this (m) is flagged as a possible disguise.
        public const float FocalDepthMismatchM = 0.25f;

        // Network IDs preserved from the original DreamCodeVR.
        public const int NidAudioInput = 98;
        public const int NidSelectedObject = 93;
        public const int NidBackendOutput = 94;
    }
}
