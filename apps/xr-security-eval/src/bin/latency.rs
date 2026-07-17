//! Validator latency microbenchmark.
//!
//! Measures the per-call wall-clock latency of the static admission check
//! (`validate_csharp_freeform_profile` at `DeployHardened`) over the benchmark corpus,
//! with warm-up and repeated measurement. Reports median / mean / p95 / p99 / max, the
//! build profile, and the repetition count. Run in RELEASE for representative numbers:
//!
//!   cargo run --release -p xr-security-eval --bin xr-latency
//!
//! Reps overridable with XR_LAT_REPS (default 3000).

use std::hint::black_box;
use std::time::Instant;

use xr_security_eval::{evaluate, load_corpus, Defence};

fn percentile(sorted_ns: &[u128], q: f64) -> u128 {
    if sorted_ns.is_empty() {
        return 0;
    }
    let idx = (((sorted_ns.len() - 1) as f64) * q).round() as usize;
    sorted_ns[idx]
}

fn main() {
    let corpus = load_corpus();
    // All 52 corpus payloads (40 attacks + 12 benign) — a representative command mix.
    let payloads: Vec<&str> = corpus.attacks.iter().map(|a| a.csharp.as_str()).collect();
    let reps: usize = std::env::var("XR_LAT_REPS")
        .ok()
        .and_then(|s| s.parse().ok())
        .unwrap_or(3000);
    let warmup: usize = 300;

    // Warm-up passes (discarded) so caches/branch-predictors/grammar tables are hot.
    for _ in 0..warmup {
        for c in &payloads {
            black_box(evaluate(black_box(c), Defence::FullyHardened));
        }
    }

    // Timed: one Instant per individual validate() call.
    let mut ns: Vec<u128> = Vec::with_capacity(reps * payloads.len());
    for _ in 0..reps {
        for c in &payloads {
            let t = Instant::now();
            let out = evaluate(black_box(c), Defence::FullyHardened);
            let elapsed = t.elapsed().as_nanos();
            black_box(out);
            ns.push(elapsed);
        }
    }

    ns.sort_unstable();
    let n = ns.len();
    let mean_ns = ns.iter().sum::<u128>() as f64 / n as f64;
    let us = |x: u128| x as f64 / 1000.0;

    let profile = if cfg!(debug_assertions) {
        "debug (NOT representative — rerun with --release)"
    } else {
        "release"
    };
    println!("=== validator latency: DeployHardened static-admission check ===");
    println!(
        "  samples : {n} ({} payloads x {reps} reps; {warmup} warm-up reps discarded)",
        payloads.len()
    );
    println!("  profile : {profile}");
    println!("  median  : {:.1} us", us(percentile(&ns, 0.50)));
    println!("  mean    : {:.1} us", mean_ns / 1000.0);
    println!("  p95     : {:.1} us", us(percentile(&ns, 0.95)));
    println!("  p99     : {:.1} us", us(percentile(&ns, 0.99)));
    println!("  max     : {:.1} us", us(*ns.last().unwrap()));
}
