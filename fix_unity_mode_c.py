import os
import glob

def replace_in_file(filepath, replacements):
    with open(filepath, "r") as f:
        text = f.read()
    
    for old, new in replacements:
        text = text.replace(old, new)
        
    with open(filepath, "w") as f:
        f.write(text)

replacements = [
    ("Mode C (bounded action plans)", "Mode B (bounded action plans)"),
    ("this is the Mode-A arm that Mode C is measured", "this is the Mode-A arm that Mode B is measured"),
    ("and Mode C produces those", "and Mode B produces those"),
    ("After a compile (Mode A/B) or plan (Mode C)", "After a compile (Mode A) or plan (Mode B)"),
    ("Mode C marks each spawn", "Mode B marks each spawn"),
    ("arbitrary generated C# (Mode A/B)", "arbitrary generated C# (Mode A)"),
    ("Mode C has no verb", "Mode B has no verb"),
    ("Mode C is the deployable architecture", "Mode B is the deployable architecture"),
    ("ModeCNetworkedDemo", "SecureModeNetworkedDemo")
]

files = [
    "unity-quest/Assets/DreamCodeVRPlus/DcvrHotAssembly.cs",
    "unity-quest/Assets/DreamCodeVRPlus/DcvrMaterials.cs",
    "unity-quest/Assets/DreamCodeVRPlus/GeneratedContentMonitor.cs",
    "unity-quest/Assets/DreamCodeVRPlus/SafeBehaviourRegistry.cs",
    "unity-quest/Assets/DreamCodeVRPlus/UserDisplacementTracker.cs",
    "unity-quest/Assets/Editor/DcvrBuild.cs"
]

for f in files:
    replace_in_file(f, replacements)

