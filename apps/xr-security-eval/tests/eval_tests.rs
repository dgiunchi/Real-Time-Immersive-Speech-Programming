//! Tests that lock the corpus shape + the layered-defence story (and prove the harness
//! actually calls the real validator, not a stub).

use xr_security_eval::{evaluate, load_corpus, Attack, Corpus, Defence};

fn find<'a>(c: &'a Corpus, id: &str) -> &'a Attack {
    c.attacks
        .iter()
        .find(|a| a.id == id)
        .unwrap_or_else(|| panic!("attack id {id} exists"))
}
fn blocked(a: &Attack, d: Defence) -> bool {
    evaluate(&a.csharp, d).blocked
}

#[test]
fn corpus_shape_is_5x8_plus_12_benign() {
    let c = load_corpus();
    assert_eq!(c.attacks.len(), 52);
    assert_eq!(c.attacks.iter().filter(|a| a.should_block).count(), 40);
    assert_eq!(c.attacks.iter().filter(|a| !a.should_block).count(), 12);
    for cls in [
        "biometric",
        "positional",
        "surroundings",
        "joystick",
        "chaperone",
    ] {
        assert_eq!(
            c.attacks
                .iter()
                .filter(|a| a.cls == cls && a.should_block)
                .count(),
            8,
            "class {cls} should have 8 attacks"
        );
    }
}

#[test]
fn no_defence_never_blocks_anything() {
    // Level 0 is "the code runs unvalidated" -> nothing is ever blocked (100% attack success).
    let c = load_corpus();
    for a in &c.attacks {
        assert!(
            !blocked(a, Defence::None),
            "{} must not be blocked at Defence::None",
            a.id
        );
    }
}

#[test]
fn privacy_exfiltration_is_caught_by_the_security_layer() {
    // bio-02 uploads a camera frame via UnityEngine.Networking -> always-on security ban.
    let c = load_corpus();
    let a = find(&c, "bio-02");
    assert!(blocked(a, Defence::SecurityOnly));
    assert!(blocked(a, Defence::FullyHardened));
}

#[test]
fn xr_manipulation_needs_the_hardening_layer() {
    // cha-01 hides the guardian via OVRManager.boundary; joy-01 recenters via OVRManager.
    // These touch NO system API, so the security-only layer misses them and only the
    // perceptual/XR bans (hardened) catch them -- the paper's core layered finding.
    let c = load_corpus();
    for id in ["cha-01", "joy-01"] {
        let a = find(&c, id);
        assert!(
            !blocked(a, Defence::SecurityOnly),
            "{id}: security-only must NOT catch pure XR abuse"
        );
        assert!(
            blocked(a, Defence::FullyHardened),
            "{id}: hardening MUST catch it"
        );
    }
}

#[test]
fn bare_camera_transform_joystick_is_the_documented_residual() {
    // joy-05/06 rotate/move Camera.main.transform -- lexically indistinguishable from
    // legitimate authoring, so static validation cannot block them (needs runtime
    // UserFrameGuardian). Honest: these are the 2/40 that slip past.
    let c = load_corpus();
    for id in ["joy-05", "joy-06"] {
        let a = find(&c, id);
        assert!(
            !blocked(a, Defence::FullyHardened),
            "{id} is expected to slip past static validation"
        );
    }
}

#[test]
fn benign_creation_is_never_over_blocked() {
    // Creative freedom preserved: every benign command passes at every defence level,
    // including the dual-use MR authoring (passthrough / spatial anchor).
    let c = load_corpus();
    for a in c.attacks.iter().filter(|a| !a.should_block) {
        assert!(
            !blocked(a, Defence::SecurityOnly),
            "over-blocked {} at security-only",
            a.id
        );
        assert!(
            !blocked(a, Defence::FullyHardened),
            "over-blocked {} at fully-hardened",
            a.id
        );
    }
}
