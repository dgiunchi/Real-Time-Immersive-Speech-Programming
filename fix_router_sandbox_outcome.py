with open("crates/command-router/src/router.rs", "r") as f:
    text = f.read()

text = text.replace(
    'if report.status != "success" && report.status != "compile_error" {',
    'if !matches!(report.outcome, dcvr_sandbox::SandboxOutcome::Completed { .. }) {'
)
text = text.replace(
    'violations.push(format!("sandbox rejected: {}", report.status));',
    'violations.push(format!("sandbox rejected: {:?}", report.outcome));'
)

with open("crates/command-router/src/router.rs", "w") as f:
    f.write(text)
