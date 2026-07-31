import "dotenv/config";
import express from "express";
import cors from "cors";
import { auth } from "./middleware/auth.js";
import { chatRouter } from "./routes/chat.js";
import { generateRouter } from "./routes/generate.js";
import { characterRouter } from "./routes/character.js";
import { worldRouter } from "./routes/world.js";
import { assetsRouter } from "./routes/assets.js";
import { settingsRouter } from "./routes/settings.js";
import { accountRouter } from "./routes/account.js";
import { getSettings, vaultLocation } from "./lib/keyvault.js";
import { bindSettingsReader } from "./aliases.js";
import { getLedger } from "./billing/metering.js";
import { TOOLS } from "./tools.js";

// aliases.ts kasayı doğrudan import etmez (döngüsel import olmasın) — okuyucuyu burada bağlıyoruz.
bindSettingsReader(getSettings);

const app = express();
app.use(cors());
app.use(express.json({ limit: "16mb" }));
app.use(auth);

app.get("/health", (_req, res) => res.json({ ok: true, tools: TOOLS.map((t) => t.name) }));

// Kullanım / fatura defteri
app.get("/v1/usage", (req, res) => res.json({ records: getLedger(req.userId) }));

app.use("/v1", chatRouter);
app.use("/v1", generateRouter);
app.use("/v1", characterRouter);
app.use("/v1", worldRouter);
app.use("/v1", assetsRouter);
app.use("/v1", settingsRouter); // /settings, /keys, /settings/test
app.use("/v1", accountRouter);  // /account, /account/grant

const PORT = Number(process.env.PORT ?? 8787);
const POOL_ENABLED = String(process.env.ALLOW_POOL_KEYS ?? "").toLowerCase() === "true";

app.listen(PORT, () => {
  console.log(`UnityAI backend http://localhost:${PORT} üzerinde çalışıyor`);
  console.log(`Araçlar: ${TOOLS.map((t) => t.name).join(", ")}`);
  console.log(`Anahtar kasası: ${vaultLocation()}`);

  // ANAHTAR MODU
  // Varsayılan: BYO zorunlu. Kullanıcı kendi anahtarını Ayarlar'dan girer, anahtar
  // kendi makinesinde şifreli durur. Sunucu sahibinin .env anahtarları KULLANILMAZ.
  //
  // ALLOW_POOL_KEYS=true yalnızca KENDİ makinende/sunucunda anlamlıdır. Bu paketi
  // dağıtırken .env'i ASLA gönderme; anahtarların kod/depo içinde bulunmamalı.
  if (POOL_ENABLED) {
    console.warn(
      "[anahtar] ALLOW_POOL_KEYS=true — kendi anahtarını kullanan kullanıcı yoksa .env havuzuna düşülür.\n" +
      "          Bu makineyi başkalarına açacaksan kimlik doğrulamayı (API_TOKENS) da aç.",
    );
  } else {
    console.log("[anahtar] BYO zorunlu — her kullanıcı kendi anahtarını Ayarlar'dan girer (havuz kapalı).");
  }
});
