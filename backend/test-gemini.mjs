// Gemini uçtan uca test: backend çalışırken `node test-gemini.mjs`
// Hataları da gösterir. Model: MODEL ortam değişkeni ile değiştirilebilir.
const BASE = process.env.BASE || "http://localhost:8787";
const MODEL = process.env.MODEL || "gemini-2.5-flash";
const PROMPT = process.argv.slice(2).join(" ") || "sahneye üst üste 3 küpten bir kule yap";

async function call(messages) {
  const res = await fetch(BASE + "/v1/chat", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ model: MODEL, messages }),
  });
  const txt = await res.text();
  if (res.status !== 200) throw new Error(`HTTP ${res.status}: ${txt}`);
  return txt.split("\n\n").filter((l) => l.startsWith("data:")).map((l) => JSON.parse(l.slice(5)));
}

(async () => {
  console.log(`Model: ${MODEL}\nİstek: "${PROMPT}"\n`);
  let history = [
    { role: "system", content: "Sen Unity editörü içinde çalışan bir AI asistanısın. Araçları kullanarak kullanıcının isteğini yerine getir. # Aktif sahne: SampleScene (boş)" },
    { role: "user", content: PROMPT },
  ];
  let turn = 0, guard = 0, cost = 0;
  while (guard++ < 10) {
    let events;
    try { events = await call(history); }
    catch (e) { console.error("İSTEK HATASI:", e.message); process.exit(1); }

    // Hata olayı geldi mi?
    const err = events.find((e) => e.type === "error");
    if (err) { console.error("❌ BACKEND HATASI:", err.message); process.exit(1); }

    const text = events.filter((e) => e.type === "token").map((e) => e.text).join("").trim();
    const calls = events.filter((e) => e.type === "tool_call");
    const bill = events.find((e) => e.type === "billing");
    if (bill) cost += bill.totalUsd || 0;

    console.log(`── Tur ${++turn} ─────────────`);
    if (text) console.log("Asistan:", text);
    for (const c of calls) console.log("  🔧", c.name, JSON.stringify(c.args));
    if (!text && calls.length === 0) {
      console.log("(boş yanıt — ham olaylar:)");
      console.log(JSON.stringify(events, null, 2));
    }
    if (calls.length === 0) { console.log("\n✅ Döngü bitti."); break; }

    history.push({ role: "assistant", content: text, toolCalls: calls.map((c) => ({ id: c.id, name: c.name, args: c.args })) });
    for (const c of calls)
      history.push({ role: "tool", toolCallId: c.id, content: JSON.stringify({ ok: true, message: `'${c.args.name || c.args.path || "nesne"}' işlendi.` }) });
  }
  console.log(`\nToplam tur: ${turn} · tahmini maliyet: $${cost.toFixed(6)}`);
})();
