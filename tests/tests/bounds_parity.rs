//! Drift guard: the Unity client re-clamps action-plan values using constants in
//! `unity/Runtime/ProtocolModels.cs`, which are a HAND-COPY of the Rust source of
//! truth (`dcvr_behaviour_dsl::bounds` + the `dcvr_protocol` NID constants).
//!
//! There is no compiler link between Rust and C#, so this test parses the C# file
//! at build time (`include_str!`) and asserts every mirrored constant still
//! matches its Rust counterpart. If someone changes a bound on one side only,
//! `cargo test` fails here instead of the two silently diverging.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use dcvr_behaviour_dsl::bounds;
use dcvr_protocol::{NID_AUDIO_INPUT, NID_BACKEND_OUTPUT, NID_SELECTED_OBJECT};

const PROTOCOL_MODELS: &str = include_str!(concat!(
    env!("CARGO_MANIFEST_DIR"),
    "/../unity/Runtime/ProtocolModels.cs"
));

/// The copy that is actually COMPILED INTO THE APK.
///
/// `unity/Runtime/` holds the drop-in scripts; `unity-quest/` is the real project, and it
/// carries its own copy. Guarding only the first one guarded the file nobody ships: the
/// budgets were raised in Rust, `unity/Runtime/` was corrected, and the headset kept
/// re-checking plans against the old, smaller limits — so the client would have refused
/// plans the backend had approved, and the parity test would still have been green.
///
/// The two files must be identical, which is a stronger and simpler invariant than
/// re-parsing every constant twice: any divergence at all is a mistake, since one is a
/// copy of the other.
const PROTOCOL_MODELS_QUEST: &str = include_str!(concat!(
    env!("CARGO_MANIFEST_DIR"),
    "/../unity-quest/Assets/DreamCodeVRPlus/ProtocolModels.cs"
));

#[test]
fn the_shipped_copy_of_the_bounds_is_identical() {
    assert_eq!(
        PROTOCOL_MODELS, PROTOCOL_MODELS_QUEST,
        "unity-quest/Assets/DreamCodeVRPlus/ProtocolModels.cs has drifted from \
         unity/Runtime/ProtocolModels.cs — the APK would re-clamp plans against \
         different bounds than the backend validates with. Copy one over the other."
    );
}

/// Extract the literal value assigned to a C# `const ... <Name> = <value>;`.
/// Returns the raw token (quotes / trailing `f` stripped).
fn cs_value(name: &str) -> String {
    let needle = format!("{name} =");
    let line = PROTOCOL_MODELS
        .lines()
        .find(|l| l.contains(&needle))
        .unwrap_or_else(|| panic!("constant `{name}` not found in ProtocolModels.cs"));
    let after = &line[line.find(&needle).unwrap() + needle.len()..];
    after
        .chars()
        .take_while(|c| *c != ';')
        .collect::<String>()
        .trim()
        .trim_matches('"')
        .trim_end_matches('f')
        .to_string()
}

fn cs_f64(name: &str) -> f64 {
    cs_value(name).parse().unwrap()
}
fn cs_u32(name: &str) -> u32 {
    cs_value(name).parse().unwrap()
}

#[test]
fn unity_protocol_models_match_rust_bounds() {
    // schema version (string)
    assert_eq!(
        cs_value("SupportedSchemaVersion"),
        bounds::SUPPORTED_SCHEMA_VERSION
    );

    // integer caps
    assert_eq!(
        cs_value("MaxActions").parse::<usize>().unwrap(),
        bounds::MAX_ACTIONS
    );
    assert_eq!(cs_u32("MaxSpawnCount"), bounds::MAX_SPAWN_COUNT);
    assert_eq!(
        cs_u32("MaxTotalSpawnedPerSession"),
        bounds::MAX_TOTAL_SPAWNED_PER_SESSION
    );
    assert_eq!(cs_u32("MaxHierarchyDepth"), bounds::MAX_HIERARCHY_DEPTH);

    // float ranges
    assert_eq!(cs_f64("ScaleMin"), bounds::SCALE_MIN);
    assert_eq!(cs_f64("ScaleMax"), bounds::SCALE_MAX);
    assert_eq!(cs_f64("MoveSpeedMax"), bounds::MOVE_SPEED_MAX);
    assert_eq!(cs_f64("MoveAmplitudeMax"), bounds::MOVE_AMPLITUDE_MAX);
    assert_eq!(
        cs_f64("MoveMaxTotalDistance"),
        bounds::MOVE_MAX_TOTAL_DISTANCE
    );
    assert_eq!(cs_f64("RotateDegPerSecMax"), bounds::ROTATE_DEG_PER_SEC_MAX);
    assert_eq!(cs_f64("PhysicsMassMin"), bounds::PHYSICS_MASS_MIN);
    assert_eq!(cs_f64("PhysicsMassMax"), bounds::PHYSICS_MASS_MAX);

    // Ubiq NetworkId channels must match the Rust protocol constants
    assert_eq!(cs_u32("NidAudioInput"), NID_AUDIO_INPUT.b);
    assert_eq!(cs_u32("NidSelectedObject"), NID_SELECTED_OBJECT.b);
    assert_eq!(cs_u32("NidBackendOutput"), NID_BACKEND_OUTPUT.b);
}
