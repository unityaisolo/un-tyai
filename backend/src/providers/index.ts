import type { ChatProvider } from "./types.js";
// NOT: MockProvider bilerek kayıtlı DEĞİL. Sahte/hazır cevaplar kullanıcıya asla
// gösterilmemeli; anahtar yoksa net hata veririz, uydurma metin akıtmayız.
import { OpenAICompatibleProvider, OpenAIProvider } from "./openai.js";
import { AnthropicProvider } from "./anthropic.js";
import { GeminiProvider } from "./gemini.js";
import { OllamaProvider } from "./ollama.js";

const providers: ChatProvider[] = [
  new OpenAIProvider(),
  new OpenAICompatibleProvider("deepseek", "https://api.deepseek.com/v1", ["deepseek"]),
  // NOT: "meta-llama/llama-4" Groq'ta; sıralama önemli — groq, openrouter'dan ÖNCE
  // olduğu için bu önekleri groq kapar, kalan "meta-llama/*" openrouter'a düşer.
  new OpenAICompatibleProvider("groq", "https://api.groq.com/openai/v1",
    ["llama-3.3-70b", "llama-3.1-8b", "llama-3.1-70b", "llama-4-", "meta-llama/llama-4", "meta-llama/llama-guard",
     "deepseek-r1-distill", "gemma2-", "qwen-qwq", "qwen/qwen3", "mixtral-8x", "moonshotai/kimi-k2", "openai/gpt-oss", "groq/compound"]),
  new OpenAICompatibleProvider("openrouter", "https://openrouter.ai/api/v1",
    ["meta-llama/", "qwen/", "nvidia/", "mistralai/", "moonshotai/", "z-ai/", "openrouter/", "google/gemma"],
    // Ücretsiz model fallback zinciri — biri rate-limit olursa OpenRouter sıradakine geçer.
    { models: [
      "meta-llama/llama-3.3-70b-instruct:free",
      "qwen/qwen3-coder:free",
      "google/gemma-3-27b-it:free",
    ] }),
  new AnthropicProvider(),
  new GeminiProvider(),
  new OllamaProvider(),
  // ÖZEL ENDPOINT — en sonda: yalnız "custom/" önekli modeller buraya düşer.
  // Taban adres kullanıcının Ayarlar'da girdiği değerden (ChatRequest.baseUrl) gelir.
  // Bu tek sağlayıcı Together, Fireworks, DeepInfra, Cerebras, Nebius, vLLM,
  // LM Studio, llama.cpp, Azure OpenAI gibi TÜM OpenAI-uyumlu servisleri açar.
  new OpenAICompatibleProvider("custom", "", ["custom/"]),
];

export function routeProvider(model: string): ChatProvider {
  const p = providers.find((p) => p.supports(model));
  if (!p) throw new Error(`Desteklenmeyen model: ${model}`);
  return p;
}

export * from "./types.js";
