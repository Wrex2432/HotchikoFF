const API_BASE = localStorage.getItem("facechinko_api_base") || `${location.protocol}//${location.hostname}:3000`;
const WS_BASE = localStorage.getItem("facechinko_ws_base") || `${location.protocol === "https:" ? "wss" : "ws"}://${location.hostname}:3000`;
const ASSET_BASE = localStorage.getItem("facechinko_asset_base") || "assets/team-balls";

const TEAMS = [
  { teamId: 0, name: "N/A", color: "#8a8f98", tempImage: "na_temp" },
  { teamId: 1, name: "Team Dana & Greggy", color: "orange", tempImage: "team_dana_greggy_temp.png" },
  { teamId: 2, name: "Team Mond & Saeid", color: "green", tempImage: "team_mond_saeid_temp.png" },
  { teamId: 3, name: "Team Jill & Alvin", color: "blue", tempImage: "team_jill_alvin_temp.png" },
  { teamId: 4, name: "Team Sam & Ninya", color: "purple", tempImage: "team_sam_ninya_temp.png" },
  { teamId: 5, name: "Team Ynna", color: "yellow", tempImage: "team_ynna_temp.png" },
  { teamId: 6, name: "Team Jasper", color: "indigo", tempImage: "team_jasper_temp.png" },
  { teamId: 7, name: "Team Jordy", color: "#00A86B", tempImage: "team_jordy_temp.png" },
  { teamId: 8, name: "Team MEDIA", color: "papayawhip", tempImage: "team_media_temp.png" },
  { teamId: 9, name: "Team STRAT", color: "royalblue", tempImage: "team_strat_temp.png" },
  { teamId: 10, name: "Team HR & ADMIN", color: "#F4D23C", tempImage: "team_hr_admin_temp.png" },
  { teamId: 11, name: "Team FINANCE", color: "limegreen", tempImage: "team_finance_temp.png" },
  { teamId: 12, name: "Team Micco", color: "#89CFF0", tempImage: "team_micco_temp.png" },
  { teamId: 13, name: "Team Bev", color: "red", tempImage: "team_bev_temp.png" },
];

const el = (id) => document.getElementById(id);
let teamIndex = 0;
let ws = null;
let uid = localStorage.getItem("fc_uid") || `fc_${Math.random().toString(36).slice(2, 10)}`;
localStorage.setItem("fc_uid", uid);

function teamImageUrl(tempImage) {
  if (!tempImage || tempImage === "na_temp") return "";
  return `${ASSET_BASE}/${tempImage}`;
}

function applyBallVisual(node, team) {
  node.style.backgroundColor = team.color;
  const url = teamImageUrl(team.tempImage);
  node.style.backgroundImage = url ? `linear-gradient(#0002,#0002), url('${url}')` : "none";
}

function renderTeam() {
  const t = TEAMS[teamIndex];
  el("teamName").textContent = t.name;
  el("teamImageName").textContent = `temp: ${t.tempImage}`;
  applyBallVisual(el("teamCircle"), t);
  el("teamCircle").style.color = t.teamId === 5 || t.teamId === 8 || t.teamId === 10 ? "#111" : "#fff";
}

function setError(msg) { el("joinError").textContent = msg || ""; }

el("teamPrev").onclick = () => { teamIndex = (teamIndex - 1 + TEAMS.length) % TEAMS.length; renderTeam(); };
el("teamNext").onclick = () => { teamIndex = (teamIndex + 1) % TEAMS.length; renderTeam(); };

async function joinFlow() {
  const code = el("roomCode").value.trim().toUpperCase();
  const team = TEAMS[teamIndex];
  setError("");
  if (!code) return setError("Please enter room code.");
  if (team.teamId === 0) return setError("Please pick a team.");

  const guestName = `P-${uid.slice(-4)}`;
  const validateRes = await fetch(`${API_BASE}/facechinko/validate?code=${encodeURIComponent(code)}&name=${encodeURIComponent(guestName)}`);
  const validate = await validateRes.json();
  if (!validate.ok) return setError("Invalid room code.");

  const selectRes = await fetch(`${API_BASE}/facechinko/select-team`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ code, uid, name: guestName, teamId: team.teamId })
  });
  const selected = await selectRes.json();
  if (!selected.ok) return setError(selected.reason || "Failed to pick team.");

  el("joinPage").classList.add("hidden");
  el("controlPage").classList.remove("hidden");
  el("controlTeamName").textContent = team.name;
  el("controlImageName").textContent = `temp image: ${team.tempImage}`;
  applyBallVisual(el("controlBall"), team);

  connectPlayerSocket(code, guestName, team.teamId);
}

function connectPlayerSocket(code, name, teamId) {
  ws = new WebSocket(WS_BASE);
  ws.onopen = () => ws.send(JSON.stringify({ type: "playerJoin", code, username: uid, fullName: name, teamId, uid }));
  ws.onmessage = async (e) => {
    const msg = JSON.parse(e.data || "{}");

    if (msg.type === "joinResult" && !msg.ok) {
      setError(msg.reason || "Could not join room.");
    }
    if (msg.type === "phase" && msg.phase === "ended") {
      checkResult(code);
    }
    if (msg.type === "powerActivated") {
      el("powerBtn").disabled = true;
      el("powerHint").textContent = "Power consumed. Wait for next pickup.";
    }
  };

  const poll = setInterval(async () => {
    const res = await fetch(`${API_BASE}/facechinko/player-state?code=${encodeURIComponent(code)}&uid=${encodeURIComponent(uid)}`);
    const state = await res.json();
    if (!state.ok) return;
    el("powerBtn").disabled = !state.player.canUsePower;
    if (state.player.canUsePower) el("powerHint").textContent = "Power is ready for your team!";
    if (state.player.phase === "ended") {
      clearInterval(poll);
      checkResult(code);
    }
  }, 1200);
}

async function checkResult(code) {
  const res = await fetch(`${API_BASE}/facechinko/player-state?code=${encodeURIComponent(code)}&uid=${encodeURIComponent(uid)}`);
  const state = await res.json();
  if (!state.ok || !state.player || state.player.phase !== "ended") return;
  const won = state.player.winningTeamId === state.player.teamId;
  el("resultText").textContent = won ? "YOU WIN 🎉" : "YOU LOSE";
}

el("joinBtn").onclick = () => joinFlow().catch((e) => setError(e.message || "Join failed"));
el("powerBtn").onclick = () => {
  if (!ws || ws.readyState !== 1) return;
  ws.send(JSON.stringify({ type: "playerMsg", payload: { kind: "powerUse" } }));
};

renderTeam();
