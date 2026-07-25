//! Every `Settings` environment variable must actually reach its field.
//!
//! `Settings::from_env` is the single place the whole backend is configured, and a
//! variable that is documented but silently ignored is worse than one that does not
//! exist — an operator sets it, sees no error, and believes the control is on. This
//! sweep sets each variable to a distinctive value, re-reads the settings, and
//! asserts the field changed, so a typo'd key or a dropped `if let Ok(..)` block
//! fails here rather than in a deployment.
//!
//! It lives in its own integration-test file on purpose: that is a separate process
//! from the unit tests, and the single `#[test]` below runs sequentially inside it,
//! so mutating the process environment cannot race another test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use dcvr_config::{AgeBand, SecurityProfile, Settings};

/// Every variable `Settings::from_env` reads. Cleared before each case so one
/// assertion can never be satisfied by a value left over from the previous one.
const ALL_VARS: &[&str] = &[
    "DCVR_LISTEN_ADDR",
    "DCVR_MODE",
    "DCVR_SECURITY_PROFILE",
    "DCVR_STT_HTTP_URL",
    "OPENAI_API_KEY",
    "OPENAI_MODEL",
    "DCVR_STT_OPENAI",
    "OPENAI_STT_MODEL",
    "DCVR_ADMIN_PORT",
    "DCVR_ADMIN_TOKEN",
    "DCVR_PERSONALIZATION_DIR",
    "DCVR_EMBED_OPENAI",
    "OPENAI_BASE_URL",
    "DCVR_STT_TIMEOUT_MS",
    "DCVR_LLM_TIMEOUT_MS",
    "DCVR_UTTERANCE_TIMEOUT_MS",
    "DCVR_MAX_INFLIGHT_PER_PEER",
    "DCVR_PER_PEER_ROUTING",
    "DCVR_CSHARP_RESEARCH",
    "DCVR_MODE_A",
    "DCVR_UBIQ_ADDR",
    "DCVR_ROOM_GUID",
    "DCVR_EMBED_ROOMSERVER",
    "DCVR_ROOMSERVER_BIND",
    "DCVR_PROFILE_TTL_SECS",
    "DCVR_ROSLYN_URL",
    "DCVR_MAX_GENERATIONS_PER_MIN",
    "DCVR_PERSONAL_SPACE_RADIUS_M",
    "DCVR_MIN_PLAN_INTERVAL_MS",
    "DCVR_COMFORT_ROTATE_MAX_DEG_S",
    "DCVR_PERCEPTUAL_HARDENING",
    "DCVR_AGE_GATING",
    "DCVR_AGE_BAND",
    "DCVR_REQUIRE_PEER_AUTH",
    "DCVR_PEER_AUTH_SECRET",
    "DCVR_BACKEND_SIGNING_SEED",
    "DCVR_PROFILE_ENC_KEY",
];

/// Serialises the tests in this file. They all mutate the PROCESS environment, and
/// Cargo runs the tests within one integration binary on parallel threads, so
/// without this one test could clear a variable another just set.
fn env_guard() -> std::sync::MutexGuard<'static, ()> {
    static LOCK: std::sync::OnceLock<std::sync::Mutex<()>> = std::sync::OnceLock::new();
    LOCK.get_or_init(|| std::sync::Mutex::new(()))
        .lock()
        // A panic in one test must not make the rest unrunnable.
        .unwrap_or_else(|poisoned| poisoned.into_inner())
}

fn clear_all() {
    for v in ALL_VARS {
        std::env::remove_var(v);
    }
}

/// Set one variable on an otherwise-clean environment and return the settings.
fn with(var: &str, value: &str) -> Settings {
    clear_all();
    std::env::set_var(var, value);
    let s = Settings::from_env().unwrap_or_else(|e| panic!("{var}={value} failed to parse: {e}"));
    clear_all();
    s
}

#[test]
fn every_settings_env_var_reaches_its_field() {
    let _g = env_guard();
    // --- defaults baseline: nothing set -> the documented defaults -------------
    clear_all();
    let d = Settings::from_env().expect("a clean environment must produce defaults");
    assert_eq!(
        d.security_profile,
        SecurityProfile::Legacy,
        "legacy is the default profile"
    );
    assert_eq!(d.openai_model, "gpt-4o-mini", "the deployed default model");
    assert!(!d.mode_a, "Mode A off by default");
    assert!(!d.embed_roomserver, "embedded RoomServer off by default");
    assert_eq!(d.profile_ttl_secs, 0, "retention sweep disabled by default");
    assert!(!d.perceptual_hardening, "creative freedom by default");
    assert!(!d.age_gating, "age gating off by default");

    // --- strings / addresses ---------------------------------------------------
    assert_eq!(
        with("DCVR_LISTEN_ADDR", "127.0.0.1:9111")
            .listen_addr
            .to_string(),
        "127.0.0.1:9111"
    );
    assert_eq!(
        with("DCVR_MODE", "action_plan_fast").mode.as_str(),
        "action_plan_fast"
    );
    assert_eq!(
        with("DCVR_STT_HTTP_URL", "https://stt.example.com/x")
            .stt_http_url
            .as_deref(),
        Some("https://stt.example.com/x")
    );
    assert_eq!(with("OPENAI_MODEL", "gpt-5.5").openai_model, "gpt-5.5");
    assert_eq!(
        with("OPENAI_BASE_URL", "https://api.example.com/v1")
            .openai_base_url
            .as_deref(),
        Some("https://api.example.com/v1")
    );
    assert_eq!(
        with("OPENAI_STT_MODEL", "gpt-4o-mini-transcribe").openai_stt_model,
        "gpt-4o-mini-transcribe"
    );
    assert_eq!(
        with("DCVR_ADMIN_TOKEN", "tok-123").admin_token.as_deref(),
        Some("tok-123")
    );
    assert_eq!(
        with("DCVR_PERSONALIZATION_DIR", "/tmp/dcvr-x").personalization_dir,
        "/tmp/dcvr-x"
    );
    assert_eq!(
        with("DCVR_UBIQ_ADDR", "10.0.0.5:8009").ubiq_addr.as_deref(),
        Some("10.0.0.5:8009")
    );
    assert_eq!(
        with("DCVR_ROOM_GUID", "99999999-9999-4999-8999-999999999999").room_guid,
        "99999999-9999-4999-8999-999999999999"
    );
    assert_eq!(
        with("DCVR_ROOMSERVER_BIND", "0.0.0.0:8123").roomserver_bind,
        "0.0.0.0:8123"
    );
    assert_eq!(
        with("DCVR_ROSLYN_URL", "http://127.0.0.1:5099/analyze")
            .roslyn_url
            .as_deref(),
        Some("http://127.0.0.1:5099/analyze")
    );
    assert_eq!(
        with("DCVR_PEER_AUTH_SECRET", "shh")
            .peer_auth_secret
            .as_deref(),
        Some("shh")
    );
    assert_eq!(
        with("DCVR_BACKEND_SIGNING_SEED", &"ab".repeat(32))
            .backend_signing_seed_hex
            .as_deref(),
        Some("ab".repeat(32).as_str())
    );
    assert_eq!(
        with("DCVR_PROFILE_ENC_KEY", &"cd".repeat(32))
            .profile_enc_key_hex
            .as_deref(),
        Some("cd".repeat(32).as_str())
    );

    // --- numbers ---------------------------------------------------------------
    assert_eq!(with("DCVR_ADMIN_PORT", "7878").admin_port, Some(7878));
    assert_eq!(with("DCVR_STT_TIMEOUT_MS", "1234").stt_timeout_ms, 1234);
    assert_eq!(with("DCVR_LLM_TIMEOUT_MS", "4321").llm_timeout_ms, 4321);
    assert_eq!(
        with("DCVR_UTTERANCE_TIMEOUT_MS", "777").utterance_timeout_ms,
        777
    );
    assert_eq!(
        with("DCVR_MAX_INFLIGHT_PER_PEER", "3").max_inflight_per_peer,
        3
    );
    assert_eq!(
        with("DCVR_PROFILE_TTL_SECS", "86400").profile_ttl_secs,
        86_400
    );
    assert_eq!(
        with("DCVR_MAX_GENERATIONS_PER_MIN", "7").max_generations_per_min,
        7
    );
    assert_eq!(
        with("DCVR_MIN_PLAN_INTERVAL_MS", "500").min_plan_interval_ms,
        500
    );
    assert!(
        (with("DCVR_PERSONAL_SPACE_RADIUS_M", "1.25").personal_space_radius_m - 1.25).abs() < 1e-9
    );
    assert!(
        (with("DCVR_COMFORT_ROTATE_MAX_DEG_S", "45").comfort_rotate_max_deg_s - 45.0).abs() < 1e-9
    );

    // --- booleans --------------------------------------------------------------
    assert!(with("DCVR_STT_OPENAI", "true").stt_openai);
    assert!(with("DCVR_EMBED_OPENAI", "true").embed_openai);
    assert!(with("DCVR_PER_PEER_ROUTING", "true").per_peer_routing);
    assert!(with("DCVR_CSHARP_RESEARCH", "true").csharp_research_dev);
    assert!(with("DCVR_MODE_A", "true").mode_a);
    assert!(with("DCVR_EMBED_ROOMSERVER", "true").embed_roomserver);
    assert!(with("DCVR_PERCEPTUAL_HARDENING", "true").perceptual_hardening);
    assert!(with("DCVR_AGE_GATING", "true").age_gating);
    assert!(with("DCVR_REQUIRE_PEER_AUTH", "true").require_peer_auth);

    // --- enums -----------------------------------------------------------------
    assert_eq!(with("DCVR_AGE_BAND", "child").age_band, AgeBand::Child);
    assert_eq!(with("DCVR_AGE_BAND", "adult").age_band, AgeBand::Adult);
    assert_eq!(
        with("DCVR_SECURITY_PROFILE", "test").security_profile,
        SecurityProfile::Test
    );

    // --- secrets: present, and never printed -----------------------------------
    let s = with("OPENAI_API_KEY", "sk-super-secret-value");
    assert!(s.openai_api_key.is_some(), "the key must be picked up");
    assert!(
        !format!("{s:?}").contains("sk-super-secret-value"),
        "a secret must never appear in Debug output"
    );

    clear_all();
}

/// The documented boolean grammar: `true/TRUE/1/yes` (any case, whitespace-tolerant)
/// is true and ANYTHING else is false — so a typo fails safe (off) rather than
/// silently enabling a control.
#[test]
fn boolean_flags_follow_the_documented_grammar() {
    let _g = env_guard();
    for truthy in ["true", "TRUE", "True", "1", "yes", "YES", " true "] {
        clear_all();
        std::env::set_var("DCVR_MODE_A", truthy);
        let s = Settings::from_env().unwrap();
        clear_all();
        assert!(s.mode_a, "{truthy:?} should parse as true");
    }
    for falsy in ["false", "0", "no", "", "off", "tru", "yes-please"] {
        clear_all();
        std::env::set_var("DCVR_MODE_A", falsy);
        let s = Settings::from_env().unwrap();
        clear_all();
        assert!(!s.mode_a, "{falsy:?} must fail safe to false");
    }
}

/// A malformed value must not silently become something surprising: numeric and
/// address fields keep their default rather than half-parsing.
#[test]
fn malformed_values_fall_back_to_defaults_rather_than_guessing() {
    let _g = env_guard();
    clear_all();
    let d = Settings::from_env().unwrap();
    let (def_stt, def_inflight) = (d.stt_timeout_ms, d.max_inflight_per_peer);

    std::env::set_var("DCVR_STT_TIMEOUT_MS", "not-a-number");
    std::env::set_var("DCVR_MAX_INFLIGHT_PER_PEER", "zero");
    let s = Settings::from_env().unwrap();
    clear_all();
    assert_eq!(
        s.stt_timeout_ms, def_stt,
        "garbage must not change the timeout"
    );
    assert_eq!(
        s.max_inflight_per_peer, def_inflight,
        "garbage must not change the cap"
    );

    // An in-flight cap below 1 would wedge the pipeline, so it is ignored.
    clear_all();
    std::env::set_var("DCVR_MAX_INFLIGHT_PER_PEER", "0");
    let s = Settings::from_env().unwrap();
    clear_all();
    assert!(s.max_inflight_per_peer >= 1, "a cap of 0 must be refused");
}
