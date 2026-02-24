const API_BASE = localStorage.getItem("facechinko_api_base") || "https://api.prologuebymetama.com";
const WS_BASE = localStorage.getItem("facechinko_ws_base") || "wss://api.prologuebymetama.com/ws";
const ASSET_BASE = localStorage.getItem("facechinko_asset_base") || "assets";

const TEAMS = [
  { teamId: 0, name: "N/A", color: "#8a8f98", imageFile: "" },
  { teamId: 1, name: "Team Dana & Greggy", color: "orange", imageFile: "TeamDana&Greggy.png" },
  { teamId: 2, name: "Team Mond & Saeid", color: "green", imageFile: "TeamMond&Saeid.png" },
  { teamId: 3, name: "Team Jill & Alvin", color: "blue", imageFile: "TeamJill&Alvin.png" },
  { teamId: 4, name: "Team Sam & Ninya", color: "purple", imageFile: "TeamSam&Ninya.png" },
  { teamId: 5, name: "Team Ynna", color: "yellow", imageFile: "TeamYnna.png" },
  { teamId: 6, name: "Team Jasper", color: "indigo", imageFile: "TeamJasper.png" },
  { teamId: 7, name: "Team Jordy", color: "#00A86B", imageFile: "TeamJordy.png" },
  { teamId: 8, name: "Team MEDIA", color: "papayawhip", imageFile: "MEDIA.png" },
  { teamId: 9, name: "Team STRAT", color: "royalblue", imageFile: "STRAT.png" },
  { teamId: 10, name: "Team HR & ADMIN", color: "#F4D23C", imageFile: "HR&ADMIN.png" },
  { teamId: 11, name: "Team FINANCE", color: "limegreen", imageFile: "FINANCE.png" },
  { teamId: 12, name: "Team Micco", color: "#89CFF0", imageFile: "TeamMicco.png" },
  { teamId: 13, name: "Team Bev", color: "red", imageFile: "TeamBev.png" },
];

const el = (id) => document.getElementById(id);
let teamIndex = 0;
let ws = null;
let uid = localStorage.getItem("fc_uid") || `fc_${Math.random().toString(36).slice(2, 10)}`;
localStorage.setItem("fc_uid", uid);

function teamImageUrl(imageFile) {
  if (!imageFile) return "";
  return `${ASSET_BASE}/${encodeURIComponent(imageFile)}`;
}

function applyBallVisual(circleNode, imageNode, team) {
  const url = teamImageUrl(team.imageFile);
  circleNode.style.backgroundColor = team.color;

  if (!url) {
    imageNode.classList.add("hidden");
    imageNode.removeAttribute("src");
    return;
  }

  imageNode.classList.remove("hidden");
  imageNode.src = url;
  imageNode.onerror = () => {
    imageNode.classList.add("hidden");
    imageNode.removeAttribute("src");
  };
}

function renderTeam() {
  const t = TEAMS[teamIndex];
  el("teamName").textContent = t.name;
  applyBallVisual(el("teamCircle"), el("teamCircleImg"), t);
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
  applyBallVisual(el("controlBall"), el("controlBallImg"), team);

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
