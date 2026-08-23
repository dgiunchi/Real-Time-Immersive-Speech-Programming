def replace_in_file(path, old, new):
    with open(path, "r") as f:
        text = f.read()
    text = text.replace(old, new)
    with open(path, "w") as f:
        f.write(text)

replace_in_file("apps/dreamcodevr-server/src/main.rs", "fake-quest-client", "test-quest-client")
replace_in_file("scripts/verify-all.sh", "fake-quest-client", "test-quest-client")

