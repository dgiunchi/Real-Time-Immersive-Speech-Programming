with open("apps/dreamcodevr-server/src/server.rs", "r") as f:
    text = f.read()

text = text.replace(
    "        auth,\n    }",
    "        auth,\n        sandbox: Some(std::sync::Arc::new(dcvr_sandbox::DockerSandboxRunner::new(\"dotnet\".to_string()))),\n    }"
)

# And make sure RouterRegistry gets sandbox
text = text.replace(
    "            .with_mode(mode)\n            .with_bus(bus.clone())",
    "            .with_mode(mode)\n            .with_bus(bus.clone())\n            .with_sandbox(services.sandbox.clone().unwrap())"
)


with open("apps/dreamcodevr-server/src/server.rs", "w") as f:
    f.write(text)
