with open("crates/command-router/src/router.rs", "r") as f:
    text = f.read()

target = """            let roslyn_ok = if lexical_ok {
                match roslyn.analyze(&cs).await {
                    Ok(v) => {
                        if !v.approved {
                            for d in &v.diagnostics {
                                violations.push(format!("roslyn: {d}"));
                            }
                        }
                        v.approved
                    }
                    Err(e) => false,
                }
            } else {
                false
            };"""

replacement = """            let mut roslyn_ok = if lexical_ok {
                match roslyn.analyze(&cs).await {
                    Ok(v) => {
                        if !v.approved {
                            for d in &v.diagnostics {
                                violations.push(format!("roslyn: {d}"));
                            }
                        }
                        v.approved
                    }
                    Err(e) => false,
                }
            } else {
                false
            };

            // Sandbox step (Phase 4 final check)
            if lexical_ok && roslyn_ok {
                if let Some(sb) = &self.sandbox {
                    let job = SandboxJob {
                        id: rid.clone(),
                        language: "csharp".to_string(),
                        code: cs.clone(),
                    };
                    let limits = ResourceLimits {
                        wall_clock: std::time::Duration::from_secs(10),
                    };
                    let report = sb.run(job, limits).await;
                    // If the sandbox explicitly rejected it (timeout/crash) but it wasn't just a Unity missing-assembly error
                    if report.status != "success" && report.status != "compile_error" {
                        roslyn_ok = false;
                        violations.push(format!("sandbox rejected: {}", report.status));
                    }
                }
            }"""

text = text.replace(target, replacement)

target2 = """            let roslyn_ok = if lexical_ok {
                match roslyn.analyze(&cs).await {
                    Ok(v) => {
                        if !v.approved {
                            for d in &v.diagnostics {
                                violations.push(format!("roslyn: {d}"));
                            }
                        }
                        v.approved
                    }
                    Err(e) => {
                        eprintln!("[router] roslyn analyzer error: {e}");
                        false
                    }
                }
            } else {
                false
            };"""

replacement2 = """            let mut roslyn_ok = if lexical_ok {
                match roslyn.analyze(&cs).await {
                    Ok(v) => {
                        if !v.approved {
                            for d in &v.diagnostics {
                                violations.push(format!("roslyn: {d}"));
                            }
                        }
                        v.approved
                    }
                    Err(e) => {
                        eprintln!("[router] roslyn analyzer error: {e}");
                        false
                    }
                }
            } else {
                false
            };

            // Sandbox step (Phase 4 final check)
            if lexical_ok && roslyn_ok {
                if let Some(sb) = &self.sandbox {
                    let job = SandboxJob {
                        id: rid.clone(),
                        language: "csharp".to_string(),
                        code: cs.clone(),
                    };
                    let limits = ResourceLimits {
                        wall_clock: std::time::Duration::from_secs(10),
                    };
                    let report = sb.run(job, limits).await;
                    if report.status != "success" && report.status != "compile_error" {
                        roslyn_ok = false;
                        violations.push(format!("sandbox rejected: {}", report.status));
                    }
                }
            }"""

text = text.replace(target2, replacement2)

with open("crates/command-router/src/router.rs", "w") as f:
    f.write(text)
