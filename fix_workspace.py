with open("Cargo.toml", "r") as f:
    lines = f.readlines()

with open("Cargo.toml", "w") as f:
    for line in lines:
        if '"apps/sandbox-runner"' not in line:
            f.write(line)
