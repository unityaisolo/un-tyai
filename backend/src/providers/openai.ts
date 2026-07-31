import type { ChatProvider, ChatRequest, StreamEvent, ToolSchema } from "./types.js";

// OpenAI-uyumlu sağlayıcı (Chat Completions). OpenAI ve DeepSeek gibi
// aynı protokolü konuşan sağlayıcılar için tek sınıf; base URL + prefix ile ayarlanır.
export class OpenAICompatibleProvider implements ChatProvider {
  constructor(
    public readonly id: string,
    private readonly baseUrl: string,
    private readonly prefixes: string[],
    private readonly extraBody: Record<string, unknown> = {},
  ) {}

  supports(model: string): boolean {
    return this.prefixes.some((p) => model.startsWith(p));
  }

  async *chat(req: ChatRequest): AsyncGenerator<StreamEvent> {
    const body = {
      // "custom/" öneki yalnızca yönlendirme içindir; sağlayıcıya gerçek model adı gider.
      model: req.model.startsWith("custom/") ? req.model.slice("custom/".length) : req.model,
      stream: true,
      stream_options: { include_usage: true },
      messages: req.messages.map((m) => {
        if (m.role === "assistant" && m.toolCalls?.length) {
          return {
            role: "assistant",
            content: m.content || null,
            tool_calls: m.toolCalls.map((tc) => ({
              id: tc.id,
              type: "function",
              function: { name: tc.name, arguments: JSON.stringify(tc.args) },
            })),
          };
        }
        if (m.role === "tool") {
          return { role: "tool", content: m.content, tool_call_id: m.toolCallId };
        }
        // Görsel ekliyse OpenAI uyumlu "content array" formatına geç
        if (m.role === "user" && m.images?.length) {
          return {
            role: "user",
            content: [
              { type: "text", text: m.content },
              ...m.images.map((b64) => ({
                type: "image_url",
                image_url: { url: b64.startsWith("data:") ? b64 : `data:image/png;base64,${b64}` },
              })),
            ],
          };
        }
        return { role: m.role, content: m.content };
      }),
      tools: req.tools.map(toOpenAITool),
      ...this.extraBody,
    };

    // "custom" sağlayıcıda taban adres kullanıcıdan gelir (Ayarlar → özel endpoint).
    const base = (req.baseUrl?.trim() || this.baseUrl).replace(/\/+$/, "");
    const res = await fetch(`${base}/chat/completions`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${req.apiKey}`,
      },
      body: JSON.stringify(body),
    });

    if (!res.ok || !res.body) {
      yield { type: "error", message: `${this.id} ${res.status}: ${await res.text()}` };
      return;
    }

    const toolAcc: Record<number, { id: string; name: string; args: string }> = {};

    for await (const data of sseLines(res.body)) {
      if (data === "[DONE]") break;
      let json: any;
      try {
        json = JSON.parse(data);
      } catch {
        continue;
      }
      const choice = json.choices?.[0];
      const delta = choice?.delta;
      if (delta?.content) yield { type: "token", text: delta.content };

      for (const tc of delta?.tool_calls ?? []) {
        const slot = (toolAcc[tc.index] ??= { id: "", name: "", args: "" });
        if (tc.id) slot.id = tc.id;
        if (tc.function?.name) slot.name = tc.function.name;
        if (tc.function?.arguments) slot.args += tc.function.arguments;
      }

      if (choice?.finish_reason === "tool_calls") {
        for (const slot of Object.values(toolAcc)) {
          yield { type: "tool_call", id: slot.id, name: slot.name, args: safeParse(slot.args) };
        }
      }

      if (json.usage) {
        yield {
          type: "usage",
          inputTokens: json.usage.prompt_tokens ?? 0,
          outputTokens: json.usage.completion_tokens ?? 0,
        };
      }
    }
    yield { type: "done" };
  }
}

// Geriye dönük uyumluluk: eski isim
export class OpenAIProvider extends OpenAICompatibleProvider {
  constructor() {
    super("openai", "https://api.openai.com/v1", ["gpt-", "o1", "o3"]);
  }
}

function toOpenAITool(t: ToolSchema) {
  return {
    type: "function",
    function: { name: t.name, description: t.description, parameters: t.parameters },
  };
}

function safeParse(s: string): Record<string, unknown> {
  try {
    return JSON.parse(s || "{}");
  } catch {
    return {};
  }
}

export async function* sseLines(body: ReadableStream<Uint8Array>): AsyncGenerator<string> {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });
    const parts = buffer.split("\n");
    buffer = parts.pop() ?? "";
    for (const line of parts) {
      const trimmed = line.trim();
      if (trimmed.startsWith("data:")) yield trimmed.slice(5).trim();
    }
  }
}
