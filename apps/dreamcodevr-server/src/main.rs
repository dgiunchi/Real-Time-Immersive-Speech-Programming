use dcvr_config::Settings;

#[tokio::main]
async fn main() {
    let settings = match Settings::from_env() {
        Ok(s) => s,
        Err(e) => {
            eprintln!("config error: {e}");
            std::process::exit(2);
        }
    };
    eprintln!(
        "dreamcodevr-server: mode={} stt={} llm={} csharp_research={}",
        settings.mode.as_str(),
        // Mirror the actual client construction in services_from_settings: the OpenAI
        // Whisper path (DCVR_STT_OPENAI + key) takes precedence over the HTTP endpoint.
        if settings.stt_openai && settings.openai_api_key.is_some() {
            "openai"
        } else if !settings.stt_is_mock() {
            "http"
        } else {
            "mock"
        },
        if settings.llm_is_mock() {
            "mock"
        } else {
            "openai"
        },
        settings.csharp_research_dev,
    );

    // Print the LAN address the Quest should point at. The Ubiq RoomServer listens on
    // TCP 8009 on ALL interfaces, so on the same Wi-Fi (e.g. an iPhone hotspot) the
    // headset connects to <this-laptop-ip>:8009. The IP changes each session on a
    // hotspot, so surfacing it here saves a lookup. Best-effort; never fatal.
    match primary_lan_ip() {
        Some(ip) => eprintln!(
            "[reachable] set the Quest app's \"Laptop server IP\" to  {ip}:8009  (RoomServer). \
             If it won't connect, open TCP 8009 in the firewall (scripts/open-firewall.sh)."
        ),
        None => eprintln!(
            "[reachable] could not auto-detect a LAN IP — run scripts/show-ip.sh to find it."
        ),
    }

    // Pull out values needed before `settings` is consumed by services_from_settings.
    let admin_port = settings.admin_port;
    let admin_token = settings.admin_token.clone();
    let ubiq = settings.ubiq_addr.clone();
    let room = settings.room_guid.clone();
    let listen_addr = settings.listen_addr;

    // LAN auto-discovery beacon: lets the headset app find this laptop on ANY network
    // (hotspot today, a different wifi tomorrow) without a baked-in IP or a rebuild.
    // UDP 8987: answers "DCVR_DISCOVER" probes with a unicast reply; also broadcasts
    // the same beacon to :8988 every 2 s for clients that just listen.
    spawn_discovery_beacon(room.clone());

    let services = dreamcodevr_server::server::services_from_settings(settings);

    // Optional web admin/debug panel (DCVR_ADMIN_PORT). Shares the live control bus
    // + personalization store with the running pipeline.
    if let Some(port) = admin_port {
        let mut state = dcvr_admin::AdminState::new(services.bus.clone()).with_token(admin_token);
        if let Some(store) = services.personalization_store.clone() {
            state = state.with_store(store);
        }
        let hooks = std::sync::Arc::new(dreamcodevr_server::server::ServerHooks::new(
            services.clone(),
        ));
        state = state.with_hooks(hooks);
        // Admin panel bind host. Default loopback (only this laptop). Set DCVR_ADMIN_BIND=0.0.0.0
        // to reach it from other devices on the same wifi (phone / Quest browser) at
        // http://<laptop-ip>:<port>. The panel is token-gated for control actions.
        let admin_ip: std::net::IpAddr = std::env::var("DCVR_ADMIN_BIND")
            .ok()
            .and_then(|s| s.trim().parse().ok())
            .unwrap_or_else(|| std::net::IpAddr::from([127, 0, 0, 1]));
        let addr = std::net::SocketAddr::new(admin_ip, port);
        tokio::spawn(async move {
            if let Err(e) = dcvr_admin::serve(addr, state).await {
                eprintln!("[admin] server error: {e}");
            }
        });
    }

    // Ubiq service-peer mode (real Unity/Quest) if DCVR_UBIQ_ADDR is set.
    if let Some(addr) = ubiq {
        if let Err(e) = dreamcodevr_server::server::run_ubiq_peer(&addr, &room, services).await {
            eprintln!("ubiq peer error: {e}");
            std::process::exit(1);
        }
        return;
    }

    // Standalone TCP listener mode (offline testing with fake-quest-client).
    eprintln!("standalone listener on {listen_addr}");
    let listener = match tokio::net::TcpListener::bind(listen_addr).await {
        Ok(l) => l,
        Err(e) => {
            eprintln!("bind error on {listen_addr}: {e}");
            std::process::exit(1);
        }
    };
    if let Err(e) = dreamcodevr_server::server::serve(listener, services).await {
        eprintln!("server error: {e}");
        std::process::exit(1);
    }
}

/// Best-effort primary LAN IPv4: ask the OS which local address it would use to reach an
/// off-link destination. This sends NO packets (a UDP `connect` only fixes the route), so
/// it works on an offline hotspot as long as a default route exists. Returns None if no
/// route can be resolved (caller falls back to scripts/show-ip.sh).
fn primary_lan_ip() -> Option<std::net::IpAddr> {
    let sock = std::net::UdpSocket::bind("0.0.0.0:0").ok()?;
    sock.connect("8.8.8.8:80").ok()?;
    sock.local_addr().ok().map(|a| a.ip())
}

/// LAN auto-discovery beacon so headsets find this server without a configured IP.
/// Protocol (plain UDP, tiny): a client either broadcasts the ASCII probe
/// `DCVR_DISCOVER` to port 8987 (we reply unicast to the sender — this path survives
/// networks that filter broadcast *to* clients), or just listens on port 8988 for the
/// 2-second broadcast beacon. Both carry the same JSON:
///   {"dcvr":1,"tcp":"<lan-ip>:8009","room":"<room-guid>"}
/// The IP is re-resolved every tick so moving the laptop to a new network updates the
/// beacon automatically. Best-effort by design: any failure only disables discovery
/// (clients can still use a configured IP), so errors log and return, never panic.
fn spawn_discovery_beacon(room: String) {
    tokio::spawn(async move {
        let sock = match tokio::net::UdpSocket::bind(("0.0.0.0", 8987u16)).await {
            Ok(s) => s,
            Err(e) => {
                eprintln!(
                    "[discovery] UDP 8987 bind failed ({e}) — auto-discovery off; \
                     headsets must use a configured IP."
                );
                return;
            }
        };
        if let Err(e) = sock.set_broadcast(true) {
            eprintln!("[discovery] set_broadcast failed ({e}); probe replies still work.");
        }
        eprintln!(
            "[discovery] beacon up (UDP 8987 probe / 8988 broadcast) — headset apps on this \
             network find the server automatically."
        );
        let mut buf = [0u8; 64];
        let mut tick = tokio::time::interval(std::time::Duration::from_secs(2));
        loop {
            tokio::select! {
                _ = tick.tick() => {
                    if let Some(ip) = primary_lan_ip() {
                        let msg = beacon_json(ip, &room);
                        let _ = sock.send_to(msg.as_bytes(), ("255.255.255.255", 8988u16)).await;
                    }
                }
                res = sock.recv_from(&mut buf) => {
                    if let Ok((n, from)) = res {
                        if &buf[..n] == b"DCVR_DISCOVER" {
                            if let Some(ip) = primary_lan_ip() {
                                let msg = beacon_json(ip, &room);
                                let _ = sock.send_to(msg.as_bytes(), from).await;
                            }
                        }
                    }
                }
            }
        }
    });
}

fn beacon_json(ip: std::net::IpAddr, room: &str) -> String {
    format!("{{\"dcvr\":1,\"tcp\":\"{ip}:8009\",\"room\":\"{room}\"}}")
}
