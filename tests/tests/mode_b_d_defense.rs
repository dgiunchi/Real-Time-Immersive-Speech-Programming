//! Mode B + Mode D defence + creative-freedom regression (the B/D architecture).
//!
//! The project ships on Mode B (Rust-validated full-freedom C#) + Mode D (sandbox
//! for untrusted code). This suite pins the two contracts that matter:
//!
//!  1. FREEDOM REGRESSION — real creative builds (house, solar system, snowman) are
//!     APPROVED under BOTH the default `CreativeFreedom` profile AND the opt-in
//!     `DeployHardened` profile. Hardening must never restrict creation.
//!  2. PERCEPTUAL DEFENCE — the XR-attack API surface (camera-view / XR rig /
//!     haptics / post-process / scene-scan) is allowed under `CreativeFreedom`
//!     (a creative effect) but REJECTED under `DeployHardened` (Mode B shippable),
//!     and is exactly the code that Mode D would contain in its gVisor sandbox.
//!
//! References: Ishii 2016; Casey 2021; Tseng 2022; Lee 2021; Fujita 2023; Krauss 2024.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use dcvr_csharp_policy::{
    semantic_screen, validate_csharp_freeform_profile, CsharpDecision, HardeningProfile,
};

fn approved(csharp: &str, profile: HardeningProfile) -> bool {
    validate_csharp_freeform_profile(csharp, profile).decision == CsharpDecision::ApproveForResearch
}

// ---- Creative builds (must pass under BOTH profiles) ----

const HOUSE: &str = r#"
public class GeneratedBehaviour : MonoBehaviour {
    void Start() {
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.transform.SetParent(transform, false);
        body.transform.localScale = new Vector3(2f, 1.5f, 2f);
        var roofL = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roofL.transform.SetParent(transform, false);
        roofL.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        roofL.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        var r = body.GetComponent<Renderer>();
        r.material.color = new Color(0.6f, 0.4f, 0.2f);
    }
}
"#;

const SOLAR_SYSTEM: &str = r#"
public class GeneratedBehaviour : MonoBehaviour {
    void Start() {
        var sun = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sun.transform.SetParent(transform, false);
        sun.transform.localScale = new Vector3(2f, 2f, 2f);
        for (int i = 1; i <= 4; i++) {
            var planet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            planet.transform.SetParent(transform, false);
            planet.transform.localPosition = new Vector3(i * 2f, 0f, 0f);
            planet.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);
            var r = planet.GetComponent<Renderer>();
            r.material.color = new Color(0.2f, 0.5f, 0.9f);
        }
    }
}
"#;

const SNOWMAN: &str = r#"
public class GeneratedBehaviour : MonoBehaviour {
    void Start() {
        float y = 0f;
        float s = 1.2f;
        for (int i = 0; i < 3; i++) {
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.transform.SetParent(transform, false);
            ball.transform.localScale = Vector3.one * s;
            ball.transform.localPosition = new Vector3(0f, y, 0f);
            var r = ball.GetComponent<Renderer>();
            r.material.color = Color.white;
            y += s * 0.8f;
            s *= 0.7f;
        }
    }
}
"#;

#[test]
fn creative_builds_pass_under_both_profiles() {
    for (name, code) in [
        ("house", HOUSE),
        ("solar_system", SOLAR_SYSTEM),
        ("snowman", SNOWMAN),
    ] {
        assert!(
            approved(code, HardeningProfile::CreativeFreedom),
            "{name} must pass under CreativeFreedom (default full freedom)"
        );
        assert!(
            approved(code, HardeningProfile::DeployHardened),
            "{name} must STILL pass under DeployHardened — hardening never limits creation"
        );
    }
}

// ---- Unambiguous DEVICE/RIG/HAPTICS/CAPTURE attacks: no legitimate creative use,
// ---- so they are lexically blocked under DeployHardened (Mode B) and contained by
// ---- Mode D. (The adversarial verification confirmed these have no creative use.)

const XR_RIG_HERDING: &str = r#"
public class GeneratedBehaviour : MonoBehaviour {
    void Update() { OVRManager.boundary.SetVisible(false); }
}
"#; // Casey 2021 chaperone / human joystick

const HAPTIC_PUNISH: &str = r#"
public class GeneratedBehaviour : MonoBehaviour {
    void Update() { OVRInput.SetControllerVibration(1f, 1f, OVRInput.Controller.RTouch); }
}
"#; // Tseng 2022 / Krauss 2024 painful head/controller feedback

const TRACKING_OFFSET: &str = r#"
public class GeneratedBehaviour : MonoBehaviour {
    void Update() { InputTracking.Recenter(); }
}
"#; // Casey 2021 / Tseng 2022 tracking-origin manipulation

const CAMERA_CAPTURE: &str = r#"
public class GeneratedBehaviour : MonoBehaviour {
    void Start() { var w = new WebCamTexture(); w.Play(); }
}
"#; // Casey 2021 camera/passthrough exfiltration

#[test]
fn device_rig_attacks_allowed_creatively_blocked_when_hardened() {
    let attacks = [
        ("xr_rig_herding", XR_RIG_HERDING),
        ("haptic_punish", HAPTIC_PUNISH),
        ("tracking_offset", TRACKING_OFFSET),
        ("camera_capture", CAMERA_CAPTURE),
    ];
    for (name, code) in attacks {
        // FREEDOM: the default profile keeps full freedom.
        assert!(
            approved(code, HardeningProfile::CreativeFreedom),
            "{name}: CreativeFreedom keeps full freedom"
        );
        // MODE B (shippable): DeployHardened rejects the unambiguous device surface.
        // MODE D: this same rejected code runs ONLY in the gVisor sandbox.
        assert!(
            !approved(code, HardeningProfile::DeployHardened),
            "{name}: DeployHardened must reject the device/rig/haptics/capture API"
        );
    }
}

// ---- View/herding/disguise vectors that a LEXER cannot separate from legitimate
// ---- creative authoring (a build's own camera, a recolour-all command, a post
// ---- effect, a self-built object). These INTENTIONALLY pass the lexical layer
// ---- under BOTH profiles — they are enforced at RUNTIME by UserFrameGuardian
// ---- (which re-asserts the tracked head/rig pose and reverts authentic-object
// ---- hides) and contained by Mode D. Asserting they pass here pins the FREEDOM
// ---- contract: hardening the validator must not block this creative authoring.

const CAMERA_TRANSFORM_FX: &str = r#"
public class GeneratedBehaviour : MonoBehaviour {
    void Update() { Camera.main.transform.Rotate(0f, 0.2f, 0f); }
}
"#; // Ishii/Casey view shift on the LIVE rig — runtime-enforced, not lexical.

const POST_PROCESS_FX: &str = r#"
public class GeneratedBehaviour : MonoBehaviour {
    void OnRenderImage(RenderTexture src, RenderTexture dst) { Graphics.Blit(src, dst); }
}
"#; // a sepia/bloom creative filter — banning it would block legitimate effects.

const SCENE_RECOLOUR: &str = r#"
public class GeneratedBehaviour : MonoBehaviour {
    void Start() {
        var all = FindObjectsOfType<Renderer>();
        for (int i = 0; i < all.Length; i++) { all[i].material.color = Color.red; }
    }
}
"#; // "recolour everything" — legitimate whole-scene styling, not a disguise.

#[test]
fn runtime_enforced_vectors_pass_lexical_layer_under_both_profiles() {
    // These are NOT lexically blocked, by design: the token alone cannot distinguish
    // attack from legitimate creative authoring. Pinning them as APPROVED documents
    // the verification's conclusion and guarantees the validator never over-blocks
    // creative work. (Their live-rig attack form is handled by the runtime guardian.)
    for (name, code) in [
        ("camera_transform_fx", CAMERA_TRANSFORM_FX),
        ("post_process_fx", POST_PROCESS_FX),
        ("scene_recolour", SCENE_RECOLOUR),
    ] {
        assert!(
            approved(code, HardeningProfile::CreativeFreedom),
            "{name}: must pass under CreativeFreedom"
        );
        assert!(
            approved(code, HardeningProfile::DeployHardened),
            "{name}: lexical layer must NOT over-block creative authoring (runtime-enforced instead)"
        );
    }
}

#[test]
fn system_access_blocked_regardless_of_profile() {
    // The always-on security layer is independent of perceptual hardening.
    let sys = r#"
public class GeneratedBehaviour : MonoBehaviour {
    void Start() { System.Diagnostics.Process.Start("rm"); }
}
"#;
    assert!(!approved(sys, HardeningProfile::CreativeFreedom));
    assert!(!approved(sys, HardeningProfile::DeployHardened));
}

// ---- Semantic screen: advisory-only, never a block. ----

#[test]
fn semantic_screen_is_advisory_only_and_freedom_safe() {
    // Benign creative commands raise NO advisory.
    assert!(semantic_screen("make a small spooky house and a spinning solar system").is_empty());
    // A dark-pattern intent is flagged (advisory) but, crucially, the screen has no
    // reject path — the build still proceeds; the system only discloses.
    let adv = semantic_screen("render a zombie onto that bystander so I punch them");
    assert!(adv.iter().any(|a| a.cluster == "swapping"));
}
