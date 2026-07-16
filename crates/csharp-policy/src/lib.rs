//! Static validator for LLM-generated C# (Phase 3, DreamCodeVR+ Mode B).
//!
//! Two layers, both fail-closed:
//! 1. [`lexical`] — a `tree-sitter` lexical/structural DENYLIST pre-filter
//!    (banned namespaces/APIs, must inherit `MonoBehaviour`, one class, size cap).
//! 2. [`consistency`] — the C# may exercise no capability the action plan does
//!    not authorize (the plan is the safety source of truth).
//!
//! HONEST LIMITATIONS: static analysis cannot defeat all obfuscation/reflection,
//! and the consistency check is a substring heuristic. The deeper allow-list
//! semantic check is a separate .NET Roslyn analyzer microservice, and risky
//! candidates are studied only in the isolated sandbox (Phase 4). This crate is
//! defence-in-depth, never the sole safety claim.

mod consistency;
mod lexical;
mod semantic;
mod verdict;

pub use consistency::consistency_check;
pub use lexical::{
    lexical_check, lexical_check_full, lexical_check_limited, lexical_check_with_profile,
    HardeningProfile, MAX_LEN,
};
pub use semantic::{semantic_screen, SemanticAdvisory};
pub use verdict::{CsharpDecision, CsharpVerdict, CsharpViolation};

use dcvr_behaviour_dsl::ActionPlan;

/// Validate a C# candidate against a (already-validated) action plan. Returns
/// [`CsharpDecision::ApproveForResearch`] only when both layers find no violation.
pub fn validate_csharp(plan: &ActionPlan, csharp: &str) -> CsharpVerdict {
    let mut violations = lexical_check(csharp);
    violations.extend(consistency_check(plan, csharp));
    let decision = if violations.is_empty() {
        CsharpDecision::ApproveForResearch
    } else {
        CsharpDecision::Reject
    };
    CsharpVerdict {
        decision,
        violations,
    }
}

/// Validate a C# candidate for the **creative / Mode-A path**, where the user has
/// full freedom to generate arbitrary scenes (a house, a tree, a particle effect,
/// ...). This applies ONLY the lexical/structural security guardrails — banned
/// namespaces/APIs (`System.IO`/`Net`/`Reflection`/`Diagnostics`/`Threading`/...,
/// `Process`/`Assembly`/`Resources`/reflection verbs/`unsafe`), must
/// inherit `MonoBehaviour`, exactly one class, and the size cap — and does NOT
/// require the C# to mirror the 6-action plan. The guardrails block malicious code
/// and over-large code; expressiveness is otherwise unrestricted. (For untrusted
/// code, the sandbox in Mode D remains the behavioural backstop.)
pub fn validate_csharp_freeform(csharp: &str) -> CsharpVerdict {
    let violations = lexical_check(csharp);
    let decision = if violations.is_empty() {
        CsharpDecision::ApproveForResearch
    } else {
        CsharpDecision::Reject
    };
    CsharpVerdict {
        decision,
        violations,
    }
}

/// Like [`validate_csharp_freeform`] but with caller-supplied size/line limits
/// (the admin panel tunes these live). `max_lines == usize::MAX` disables the
/// line check.
pub fn validate_csharp_freeform_limited(
    csharp: &str,
    max_chars: usize,
    max_lines: usize,
) -> CsharpVerdict {
    let violations = lexical_check_limited(csharp, max_chars, max_lines);
    let decision = if violations.is_empty() {
        CsharpDecision::ApproveForResearch
    } else {
        CsharpDecision::Reject
    };
    CsharpVerdict {
        decision,
        violations,
    }
}

/// Creative-path C# validation under an explicit [`HardeningProfile`] — the Mode-B
/// deployment switch. `CreativeFreedom` (default) preserves FULL creative freedom
/// (system-access bans only). `DeployHardened` ADDS the perceptual/embodied-attack
/// API bans (XR rig / camera-view / haptics / second-display / scene-scan) for a
/// shippable embodied-safe build, without otherwise limiting expressiveness.
pub fn validate_csharp_freeform_profile(csharp: &str, profile: HardeningProfile) -> CsharpVerdict {
    let violations = lexical_check_with_profile(csharp, profile);
    let decision = if violations.is_empty() {
        CsharpDecision::ApproveForResearch
    } else {
        CsharpDecision::Reject
    };
    CsharpVerdict {
        decision,
        violations,
    }
}

/// Creative-path C# validation with BOTH live size/line limits and a hardening
/// profile (the admin panel can tune limits and flip the deploy-hardened switch).
pub fn validate_csharp_freeform_limited_profile(
    csharp: &str,
    max_chars: usize,
    max_lines: usize,
    profile: HardeningProfile,
) -> CsharpVerdict {
    let violations = lexical_check_full(csharp, max_chars, max_lines, profile);
    let decision = if violations.is_empty() {
        CsharpDecision::ApproveForResearch
    } else {
        CsharpDecision::Reject
    };
    CsharpVerdict {
        decision,
        violations,
    }
}

#[cfg(test)]
#[allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]
mod profile_tests {
    use super::*;

    // A real creative build (a small structure): CreatePrimitive + transform +
    // material. Must pass under BOTH profiles — full creative freedom is preserved
    // even with the perceptual hardening armed.
    const CREATIVE_BUILD: &str = r#"
public class GeneratedBehaviour : MonoBehaviour {
    void Start() {
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.transform.SetParent(transform, false);
        body.transform.localScale = new Vector3(1f, 1.5f, 1f);
        var r = body.GetComponent<Renderer>();
        r.material.color = Color.red;
        var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roof.transform.SetParent(transform, false);
        roof.transform.localPosition = new Vector3(0f, 1.2f, 0f);
    }
}
"#;

    // A haptics device attack (Tseng 2022 / Krauss 2024 painful feedback). An
    // unambiguous device API: legal under CreativeFreedom, banned under DeployHardened.
    const HAPTICS_ATTACK: &str = r#"
public class GeneratedBehaviour : MonoBehaviour {
    void Update() {
        OVRHaptics.RightChannel.Mix(default);
    }
}
"#;

    // A creative camera effect on an AUTHOR-CREATED camera (a diorama / preview shot)
    // PLUS a RenderTexture (a reflection/minimap). The adversarial verification showed
    // banning these tokens wrongly blocks legitimate authoring — so they must pass
    // BOTH profiles. The live-rig version of this attack is caught at RUNTIME
    // (UserFrameGuardian), not lexically.
    const CREATIVE_CAMERA_FX: &str = r#"
public class GeneratedBehaviour : MonoBehaviour {
    void Start() {
        var camGo = new GameObject("PreviewCam");
        camGo.transform.SetParent(transform, false);
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 30f;
        cam.targetTexture = new RenderTexture(128, 128, 16);
    }
}
"#;

    // An XR-rig / chaperone manipulation (Casey 2021 human joystick / chaperone).
    const XR_RIG_ATTACK: &str = r#"
public class GeneratedBehaviour : MonoBehaviour {
    void Update() {
        OVRManager.boundary.SetVisible(false);
    }
}
"#;

    // Genuine system access — must be rejected under BOTH profiles.
    const SYSTEM_ACCESS: &str = r#"
public class GeneratedBehaviour : MonoBehaviour {
    void Start() {
        System.IO.File.WriteAllText("/tmp/x", "y");
    }
}
"#;

    fn approved(v: &CsharpVerdict) -> bool {
        v.decision == CsharpDecision::ApproveForResearch
    }

    #[test]
    fn creative_build_passes_both_profiles() {
        // FREEDOM CONTRACT: a creative structure compiles even when the perceptual
        // hardening is on. Hardening never restricts creation.
        assert!(approved(&validate_csharp_freeform_profile(
            CREATIVE_BUILD,
            HardeningProfile::CreativeFreedom
        )));
        assert!(approved(&validate_csharp_freeform_profile(
            CREATIVE_BUILD,
            HardeningProfile::DeployHardened
        )));
    }

    #[test]
    fn haptics_device_api_only_blocked_when_hardened() {
        // Default = freedom.
        assert!(approved(&validate_csharp_freeform_profile(
            HAPTICS_ATTACK,
            HardeningProfile::CreativeFreedom
        )));
        // Deploy-hardened = blocked (unambiguous device API, no creative use).
        assert!(!approved(&validate_csharp_freeform_profile(
            HAPTICS_ATTACK,
            HardeningProfile::DeployHardened
        )));
    }

    #[test]
    fn creative_camera_effects_pass_both_profiles() {
        // FREEDOM CONTRACT (verification-driven): authoring a preview camera, a custom
        // FOV, and a RenderTexture for a reflection/minimap are legitimate creative
        // builds and MUST pass even under DeployHardened. The lexer cannot distinguish
        // these from a live-rig attack, so the live-rig case is runtime-enforced, not
        // lexically banned — and creation stays free.
        assert!(approved(&validate_csharp_freeform_profile(
            CREATIVE_CAMERA_FX,
            HardeningProfile::CreativeFreedom
        )));
        assert!(approved(&validate_csharp_freeform_profile(
            CREATIVE_CAMERA_FX,
            HardeningProfile::DeployHardened
        )));
    }

    #[test]
    fn xr_rig_manipulation_only_blocked_when_hardened() {
        assert!(approved(&validate_csharp_freeform_profile(
            XR_RIG_ATTACK,
            HardeningProfile::CreativeFreedom
        )));
        assert!(!approved(&validate_csharp_freeform_profile(
            XR_RIG_ATTACK,
            HardeningProfile::DeployHardened
        )));
    }

    #[test]
    fn system_access_blocked_under_both_profiles() {
        assert!(!approved(&validate_csharp_freeform_profile(
            SYSTEM_ACCESS,
            HardeningProfile::CreativeFreedom
        )));
        assert!(!approved(&validate_csharp_freeform_profile(
            SYSTEM_ACCESS,
            HardeningProfile::DeployHardened
        )));
    }

    // FREEDOM CONTRACT (adversarial-verify finding): the Quest-3 MIXED-REALITY and
    // body-interaction APIs are DUAL-USE content-authoring primitives — passthrough
    // compositing, spatial anchors / scene model, and hand/eye/face tracking are how you
    // *build* MR content ("show my room and anchor a lamp", gaze/hand interaction). They
    // must PASS under BOTH profiles; banning them lexically would over-block benign
    // creative builds. (Covert misuse is a runtime/disclosure concern, and exfiltration
    // is already blocked by the System.Net ban.)
    #[test]
    fn dual_use_mr_and_interaction_apis_pass_both_profiles() {
        let cases = [
            ("eye-gaze", "var g = GetComponent<OVREyeGaze>();"),
            ("face", "var f = GetComponent<OVRFaceExpressions>();"),
            ("hand", "var h = GetComponent<OVRHand>();"),
            ("skeleton", "var s = GetComponent<OVRSkeleton>();"),
            ("scene-manager", "var m = GetComponent<OVRSceneManager>();"),
            ("scene-anchor", "var a = GetComponent<OVRSceneAnchor>();"),
            (
                "spatial-anchor",
                "var a = GetComponent<OVRSpatialAnchor>();",
            ),
            (
                "passthrough",
                "var p = GetComponent<OVRPassthroughLayer>();",
            ),
        ];
        for (name, body) in cases {
            let code = format!(
                "public class GeneratedBehaviour : MonoBehaviour {{ void Start() {{ {body} }} }}"
            );
            assert!(
                approved(&validate_csharp_freeform_profile(
                    &code,
                    HardeningProfile::CreativeFreedom
                )),
                "{name}: must pass under CreativeFreedom"
            );
            assert!(
                approved(&validate_csharp_freeform_profile(
                    &code,
                    HardeningProfile::DeployHardened
                )),
                "{name}: dual-use MR/interaction API must NOT be over-blocked under DeployHardened"
            );
        }
    }

    // The unambiguous XR device-ENUMERATION APIs (fingerprint the hardware, not content):
    // free by default, blocked only under DeployHardened. `InputDevices` also covers a
    // bare-identifier subsequence gap — `InputDevices.x` shows no [UnityEngine, XR]
    // namespace at the call site, so only the identifier ban catches it.
    #[test]
    fn xr_device_enumeration_apis_only_blocked_when_hardened() {
        let cases = [
            ("input-devices", "InputDevices.GetDeviceAtXRNode(default);"),
            ("xr-input-subsystem", "XRInputSubsystem x = default;"),
        ];
        for (name, body) in cases {
            let code = format!(
                "public class GeneratedBehaviour : MonoBehaviour {{ void Update() {{ {body} }} }}"
            );
            assert!(
                approved(&validate_csharp_freeform_profile(
                    &code,
                    HardeningProfile::CreativeFreedom
                )),
                "{name}: free by default (not system access)"
            );
            assert!(
                !approved(&validate_csharp_freeform_profile(
                    &code,
                    HardeningProfile::DeployHardened
                )),
                "{name}: device-enumeration API must be blocked under DeployHardened"
            );
        }
    }
}
