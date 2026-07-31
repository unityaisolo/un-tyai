import type { ChatProvider, ChatRequest, StreamEvent, ToolSchema } from "./types.js";

// Ollama (yerel) - streaming + tool calling. Ücretsiz, internet gerekmez.
// Model adı "ollama/<model>" biçiminde gelir (ör. "ollama/llama3.1"); prefix soyulur.
// Ollama NDJSON döner (SSE değil): her satır bir JSON nesnesi.
export class OllamaProvider implements ChatProvider {
  readonly id = "ollama";
  private readonly host = (process.env.OLLAMA_HOST ?? "http://localhost:11434").replace(/\/$/, "");

  supports(model: string): boolean {
    return model.startsWith("ollama/") || model.startsWith("ollama:");
  }

  async *chat(req: ChatRequest): AsyncGenerator<StreamEvent> {
    const model = req.model.replace(/^ollama[/:]/, "");

    const messages = req.messages.map((m) => {
      if (m.role === "assistant" && m.toolCalls?.length) {
        return {
          role: "assistant",
          content: m.content ?? "",
          tool_calls: m.toolCalls.map((tc) => ({
            function: { name: tc.name, arguments: tc.args },
          })),
        };
      }
      if (m.role === "tool") return { role: "tool", content: m.content };
      return { role: m.role, content: m.content };
    });

    // OLLAMA_NUM_GPU=0 ise GPU tamamen kapatılır (CUDA sürücü sorunları için CPU'ya zorlar).
    const options: Record<string, unknown> = {};
    if (process.env.OLLAMA_NUM_GPU !== undefined && process.env.OLLAMA_NUM_GPU !== "")
      options.num_gpu = Number(process.env.OLLAMA_NUM_GPU);

    const body = {
      model,
      stream: true,
      messages,
      tools: req.tools.map(toOllamaTool),
      ...(Object.keys(options).length ? { options } : {}),
    };

    let res: Response;
    try {
      res = await fetch(this.host + "/api/chat", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
    } catch (e) {
      yield {
        type: "error",
        message:
          `Ollama'ya bağlanılamadı (${this.host}). Ollama kurulu ve çalışıyor mu? ` +
          `Kurulum: https://ollama.com · Model: 'ollama pull ${model}'. Detay: ${e instanceof Error ? e.message : e}`,
      };
      return;
    }

    if (!res.ok || !res.body) {
      yield { type: "error", message: `Ollama ${res.status}: ${await res.text()}` };
      return;
    }

    let idCounter = 0;
    for await (const line of ndjsonLines(res.body)) {
      let ev: any;
      try {
        ev = JSON.parse(line);
      } catch {
        continue;
      }
      const msg = ev.message;
      if (msg?.content) yield { type: "token", text: msg.content };
      for (const tc of msg?.tool_calls ?? []) {
        yield {
          type: "tool_call",
          id: "call_" + ++idCounter + "_" + Date.now(),
          name: tc.function?.name ?? "",
          args: (tc.function?.arguments as Record<string, unknown>) ?? {},
        };
      }
      if (ev.done) {
        yield {
          type: "usage",
          inputTokens: ev.prompt_eval_count ?? 0,
          outputTokens: ev.eval_count ?? 0,
        };
      }
    }
    yield { type: "done" };
  }
}

function toOllamaTool(t: ToolSchema) {
  return {
    type: "function",
    function: { name: t.name, description: t.description, parameters: t.parameters },
  };
}

// NDJSON: satır satır JSON. ReadableStream'i satırlara böler.
async function* ndjsonLines(body: ReadableStream<Uint8Array>): AsyncGenerator<string> {
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
      if (trimmed.length > 0) yield trimmed;
    }
  }
  if (buffer.trim().length > 0) yield buffer.trim();
}
