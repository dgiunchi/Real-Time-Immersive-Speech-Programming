with open("apps/dreamcodevr-server/src/server.rs", "r") as f:
    text = f.read()

text = text.replace(
    "    Services {\n        stt,",
    "    Services {\n        mode: settings.mode,\n        stt,"
)

text = text.replace(
    "let mut r = Router::new().with_bus(bus.clone());",
    """let router_mode = match services.mode {
                dcvr_config::RunMode::Baseline => dcvr_command_router::Mode::Baseline,
                dcvr_config::RunMode::Secure => dcvr_command_router::Mode::Secure,
            };
            let mut r = Router::new().with_mode(router_mode).with_bus(bus.clone());"""
)

text = text.replace(
    "let mut router = Router::new().with_bus(services.bus.clone());",
    """let router_mode = match services.mode {
            dcvr_config::RunMode::Baseline => dcvr_command_router::Mode::Baseline,
            dcvr_config::RunMode::Secure => dcvr_command_router::Mode::Secure,
        };
        let mut router = Router::new().with_mode(router_mode).with_bus(services.bus.clone());"""
)

with open("apps/dreamcodevr-server/src/server.rs", "w") as f:
    f.write(text)
