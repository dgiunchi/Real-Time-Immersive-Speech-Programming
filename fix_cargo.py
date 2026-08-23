with open("Cargo.toml", "r") as f:
    text = f.read()

text = text.replace(
    '    "apps/model-bench"',
    '    "apps/model-bench",\n    "apps/sandbox-runner"'
)

with open("Cargo.toml", "w") as f:
    f.write(text)
