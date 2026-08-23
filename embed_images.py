with open("README.md", "r") as f:
    text = f.read()

# Hero Banner
text = text.replace(
    "# DreamCodeVR+ (Secure Architecture)\n",
    "# DreamCodeVR+ (Secure Architecture)\n\n![DreamCodeVR+ Banner](docs/images/hero_banner.png)\n"
)

# Guardrail
text = text.replace(
    "## 🏗️ Architecture\n",
    "## 🏗️ Architecture\n\n![Guardrail Concept](docs/images/guardrail_concept.png)\n"
)

# VR Environment
text = text.replace(
    "## 🥽 On a Meta Quest 3\n",
    "## 🥽 On a Meta Quest 3\n\n![VR Environment](docs/images/vr_environment.png)\n"
)

with open("README.md", "w") as f:
    f.write(text)
