with open("apps/test-quest-client/Cargo.toml", "r") as f:
    text = f.read()

text = text.replace('name = "fake-quest-client"', 'name = "test-quest-client"')

with open("apps/test-quest-client/Cargo.toml", "w") as f:
    f.write(text)

with open("Cargo.toml", "r") as f:
    text = f.read()

text = text.replace('"apps/fake-quest-client",', '"apps/test-quest-client",')

with open("Cargo.toml", "w") as f:
    f.write(text)
