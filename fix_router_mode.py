with open("crates/command-router/src/router.rs", "r") as f:
    text = f.read()
    
text = text.replace(
    "pub fn with_bus(mut self, bus: ControlBus) -> Self {",
    "pub fn with_mode(mut self, mode: crate::request::Mode) -> Self {\n        self.mode = mode;\n        self\n    }\n\n    pub fn with_bus(mut self, bus: ControlBus) -> Self {"
)

with open("crates/command-router/src/router.rs", "w") as f:
    f.write(text)
