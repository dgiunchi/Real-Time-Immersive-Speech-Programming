import re

file_path = "crates/command-router/src/router.rs"
with open(file_path, "r") as f:
    text = f.read()

# 1. Add to imports
if "SandboxRunner" not in text:
    text = text.replace(
        "use dcvr_csharp_policy::{",
        "use dcvr_sandbox::{SandboxJob, ResourceLimits, SandboxRunner};\nuse dcvr_csharp_policy::{"
    )

# 2. Add to Router struct
if "sandbox: Option<Arc<dyn SandboxRunner>>" not in text:
    text = text.replace(
        "    pub personalizer: Option<Arc<Personalizer>>,\n",
        "    pub personalizer: Option<Arc<Personalizer>>,\n    pub sandbox: Option<std::sync::Arc<dyn SandboxRunner>>,\n"
    )

# 3. Add to Router::new()
if "sandbox: None," not in text:
    text = text.replace(
        "            personalizer: None,\n",
        "            personalizer: None,\n            sandbox: None,\n"
    )

# 4. Add builder method
if "pub fn with_sandbox" not in text:
    text = text.replace(
        "    pub fn with_personalizer(mut self, p: Arc<Personalizer>) -> Self {",
        "    pub fn with_sandbox(mut self, s: std::sync::Arc<dyn SandboxRunner>) -> Self {\n        self.sandbox = Some(s);\n        self\n    }\n\n    pub fn with_personalizer(mut self, p: Arc<Personalizer>) -> Self {"
    )

with open(file_path, "w") as f:
    f.write(text)
