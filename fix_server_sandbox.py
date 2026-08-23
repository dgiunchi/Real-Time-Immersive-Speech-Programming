with open("apps/dreamcodevr-server/src/app.rs", "r") as f:
    text = f.read()

text = text.replace(
    "    pub llm: Arc<dyn LlmClient>,\n",
    "    pub llm: Arc<dyn LlmClient>,\n    pub sandbox: Option<std::sync::Arc<dyn dcvr_sandbox::SandboxRunner>>,\n"
)
with open("apps/dreamcodevr-server/src/app.rs", "w") as f:
    f.write(text)


with open("apps/dreamcodevr-server/src/server.rs", "r") as f:
    text = f.read()

# Pass sandbox to RouterRegistry
text = text.replace(
    "            .with_mode(mode)\n            .with_bus(bus.clone())",
    "            .with_mode(mode)\n            .with_bus(bus.clone())\n            .with_sandbox(services.sandbox.clone().unwrap())"
)

# Initialize in services_from_settings
if "sandbox: None" not in text:
    text = text.replace(
        "        rag,\n    }",
        "        rag,\n        sandbox: Some(std::sync::Arc::new(dcvr_sandbox::DockerSandboxRunner::new(\"dotnet\".to_string()))),\n    }"
    )

with open("apps/dreamcodevr-server/src/server.rs", "w") as f:
    f.write(text)

with open("apps/dreamcodevr-server/Cargo.toml", "r") as f:
    text = f.read()
if "dcvr-sandbox" not in text:
    text = text.replace(
        'dcvr-command-router = { path = "../../crates/command-router" }',
        'dcvr-command-router = { path = "../../crates/command-router" }\ndcvr-sandbox = { path = "../../crates/sandbox" }'
    )
with open("apps/dreamcodevr-server/Cargo.toml", "w") as f:
    f.write(text)
