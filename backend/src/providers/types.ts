// Ortak sağlayıcı arayüzü. Her LLM sağlayıcısı bunu implemente eder.
// Unity tarafı sağlayıcıyı bilmez; sadece backend protokolünü konuşur.

export interface ToolCall {
  id: string;
  name: string;
  args: Record<string, unknown>;
}

export interface ChatMessage {
  role: "system" | "user" | "assistant" | "tool";
  content: string;
  toolCalls?: ToolCall[];
  toolCallId?: string;
  /** Kullanıcının eklediği görseller — base64 PNG (data: öneki OLMADAN). */
  images?: string[];
}

export interface ToolSchema {
  name: string;
  description: string;
  parameters: Record<string, unknown>;
}

export type StreamEvent =
  | { type: "token"; text: string }
  | { type: "tool_call"; id: string; name: string; args: Record<string, unknown> }
  | { type: "usage"; inputTokens: number; outputTokens: number }
  | { type: "done" }
  | { type: "error"; message: string };

export interface ChatRequest {
  model: string;
  messages: ChatMessage[];
  tools: ToolSchema[];
  apiKey: string;
  /**
   * İSTEK BAŞINA taban adres — yalnız "custom" sağlayıcı için kullanılır.
   * Kullanıcı Ayarlar'dan kendi OpenAI-uyumlu endpoint'ini girer
   * (Together / Fireworks / DeepInfra / Cerebras / vLLM / LM Studio / Azure ...).
   * Verilmezse sağlayıcının kendi sabit adresi kullanılır.
   */
  baseUrl?: string;
}

export interface ChatProvider {
  readonly id: string;
  supports(model: string): boolean;
  chat(req: ChatRequest): AsyncGenerator<StreamEvent>;
}
