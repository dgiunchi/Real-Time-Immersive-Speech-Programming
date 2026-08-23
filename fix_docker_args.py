with open("apps/dreamcodevr-server/src/server.rs", "r") as f:
    text = f.read()

text = text.replace(
    'DockerSandboxRunner::new("dotnet".to_string())',
    'DockerSandboxRunner::new("dcvr-sandbox-harness:local", Vec::new())'
)

with open("apps/dreamcodevr-server/src/server.rs", "w") as f:
    f.write(text)
