import os
import re

file_path = "unity-quest/Assets/DreamCodeVRPlus/SecureModeNetworkedDemo.cs"
with open(file_path, "r") as f:
    text = f.read()

# Change class name
text = text.replace("public class ModeCNetworkedDemo : MonoBehaviour", "public class SecureModeNetworkedDemo : MonoBehaviour")

# Remove/update mentions of Mode C
text = text.replace("[ModeC-Net]", "[SecureMode-Net]")
text = text.replace("Mode C, the default", "Mode B, the secure default")
text = text.replace("Mode C creates into a GROUP too", "Mode B creates into a GROUP too")
text = text.replace("Mode C (DCVR_MODE_C=true)", "Mode B (Secure)")
text = text.replace("Mode A on hardware that cannot compile", "Baseline C# on hardware that cannot compile")
text = text.replace("Otherwise (Mode C, the default): apply the safe action plan", "Otherwise (Mode B, the default): apply the safe action plan")
text = text.replace("Mode A (DCVR_MODE_A=true)", "Mode A (Baseline)")
text = text.replace("Mode C (bounded action plans)", "Mode B (bounded action plans)")

with open(file_path, "w") as f:
    f.write(text)

# Also rename the .meta file!
os.rename("unity-quest/Assets/DreamCodeVRPlus/ModeCNetworkedDemo.cs.meta", "unity-quest/Assets/DreamCodeVRPlus/SecureModeNetworkedDemo.cs.meta")

