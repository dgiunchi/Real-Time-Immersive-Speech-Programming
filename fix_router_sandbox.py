with open("crates/command-router/src/router.rs", "r") as f:
    text = f.read()

text = text.replace(
    "    personalizer: Option<Arc<Personalizer>>,\n",
    "    personalizer: Option<Arc<Personalizer>>,\n    sandbox: Option<std::sync::Arc<dyn SandboxRunner>>,\n"
)

with open("crates/command-router/src/router.rs", "w") as f:
    f.write(text)
