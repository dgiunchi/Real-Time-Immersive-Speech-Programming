use dcvr_config::Settings;

/// The Ubiq RoomServer's conventional TCP port, used when nothing more specific is
/// known (no embedded server and no explicit `DCVR_UBIQ_ADDR`).
const DEFAULT_ROOMSERVER_PORT: u16 = 8009;

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
        "dreamcodevr-server: mode={} stt={} llm={}",
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

    );

    // Pull out values needed before `settings` is consumed by services_from_settings.
    let admin_port = settings.admin_port;
    let admin_token = settings.admin_token.clone();
    let mut ubiq = settings.ubiq_addr.clone();
    let room = settings.room_guid.clone();
    let listen_addr = settings.listen_addr;
    let embed_roomserver = settings.embed_roomserver;
    let profile_ttl_secs = settings.profile_ttl_secs;
    let roomserver_bind = settings.roomserver_bind.clone();

    let services = dreamcodevr_server::server::services_from_settings(settings);

    // Retention sweep (GDPR Art. 5(1)(e)): drop personalization profiles that have
    // not been touched within the TTL. Disabled by default (ttl = 0) so behaviour is
    // unchanged unless an operator opts in. Without this the store keeps profiles
    // forever, which is exactly what a retention limit is supposed to prevent.
    if profile_ttl_secs > 0 {
        if let Some(store) = services.personalization_store.clone() {
            // Sweep hourly, or every ttl/2 when the TTL is shorter than that, so a
            // short TTL is still honoured promptly. Once at startup, then on a timer.
            let period = std::time::Duration::from_secs((profile_ttl_secs / 2).clamp(1, 3600));
            eprintln!(
                "[retention] purging personalization profiles older than {profile_ttl_secs}s \
                 (sweep every {}s)",
                period.as_secs()
            );
            tokio::spawn(async move {
                let mut tick = tokio::time::interval(period);
                loop {
                    tick.tick().await;
                    let n = store.purge_expired(profile_ttl_secs, std::time::SystemTime::now());
                    if n > 0 {
                        eprintln!("[retention] purged {n} expired personalization profile(s)");
                    }
                }
            });
        }
    }

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

    // Embedded RoomServer mode (DCVR_EMBED_ROOMSERVER): run the Rust RoomServer
    // in-process so there is NO external Node.js relay — one binary is the whole
    // system (switchboard + pipeline + guardrail). The pipeline then joins as a
    // normal Ubiq peer over loopback; a headset dials this laptop's :8009 and
    // lands in the same room, so its audio is relayed to the pipeline and the
    // NID-94 answer is relayed back. Held for the process lifetime.
    let _embedded_room = if embed_roomserver {
        match dcvr_roomserver::RoomServerHandle::bind(&roomserver_bind).await {
            Ok(handle) => {
                let port = handle.local_addr().port();
                eprintln!(
                    "[embedded] Rust RoomServer listening on {} — no Node.js needed. \
                     Pipeline joins over loopback; headsets dial <laptop-ip>:{port}.",
                    handle.local_addr()
                );
                // Point the pipeline peer at the embedded server over loopback,
                // overriding any DCVR_UBIQ_ADDR.
                ubiq = Some(format!("127.0.0.1:{port}"));
                Some(handle)
            }
            Err(e) => {
                eprintln!("embedded roomserver bind on {roomserver_bind} failed: {e}");
                std::process::exit(1);
            }
        }
    } else {
        None
    };

    // LAN auto-discovery beacon: lets the headset app find this laptop on ANY network
    // (hotspot today, a different wifi tomorrow) without a baked-in IP or a rebuild.
    // UDP 8987: answers "DCVR_DISCOVER" probes with a unicast reply; also broadcasts
    // the same beacon to :8988 every 2 s for clients that just listen.
    //
    // Started only AFTER the RoomServer address is known, and advertising THAT port:
    // announcing a hardcoded 8009 while actually listening elsewhere would send every
    // headset to a closed port.
    let advertised_port = _embedded_room
        .as_ref()
        .map(|h| h.local_addr().port())
        .or_else(|| {
            ubiq.as_deref()
                .and_then(|a| a.rsplit_once(':'))
                .and_then(|(_, p)| p.parse().ok())
        })
        .unwrap_or(DEFAULT_ROOMSERVER_PORT);
    spawn_discovery_beacon(room.clone(), advertised_port);

    // Print the LAN address the Quest should point at, using the port we are ACTUALLY
    // serving on (see above). The IP changes every session on a hotspot, so surfacing
    // it here saves a lookup. Best-effort; never fatal.
    match primary_lan_ip() {
        Some(ip) => eprintln!(
            "[reachable] set the Quest app's \"Laptop server IP\" to  {ip}:{advertised_port}  \
             (RoomServer). If it won't connect, open TCP {advertised_port} in the firewall \
             (scripts/open-firewall.sh)."
        ),
        None => eprintln!(
            "[reachable] could not auto-detect a LAN IP — run scripts/show-ip.sh to find it."
        ),
    }

    // Ubiq service-peer mode (real Unity/Quest) if DCVR_UBIQ_ADDR is set, or the
    // loopback address of the embedded RoomServer above.
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
///   {"dcvr":1,"tcp":"<lan-ip>:<roomserver-port>","room":"<room-guid>"}
/// The IP is re-resolved every tick so moving the laptop to a new network updates the
/// beacon automatically. Best-effort by design: any failure only disables discovery
/// (clients can still use a configured IP), so errors log and return, never panic.
fn spawn_discovery_beacon(room: String, port: u16) {
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
                        let msg = beacon_json(ip, &room, port);
                        let _ = sock.send_to(msg.as_bytes(), ("255.255.255.255", 8988u16)).await;
                    }
                }
                res = sock.recv_from(&mut buf) => {
                    if let Ok((n, from)) = res {
                        if &buf[..n] == b"DCVR_DISCOVER" {
                            if let Some(ip) = primary_lan_ip() {
                                let msg = beacon_json(ip, &room, port);
                                let _ = sock.send_to(msg.as_bytes(), from).await;
                            }
                        }
                    }
                }
            }
        }
    });
}

fn beacon_json(ip: std::net::IpAddr, room: &str, port: u16) -> String {
    format!("{{\"dcvr\":1,\"tcp\":\"{ip}:{port}\",\"room\":\"{room}\"}}")
}
