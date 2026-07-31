import type { ChatProvider, ChatRequest, StreamEvent, ToolSchema } from "./types.js";
import { sseLines } from "./openai.js";

// Google Gemini (Generative Language API) - streaming + function calling.
// SSE için alt=sse kullanır. Test sırasında ucuz/hızlı bir seçenek.
export class GeminiProvider implements ChatProvider {
  readonly id = "gemini";
  supports(model: string): boolean {
    return model.startsWith("gemini");
  }

  async *chat(req: ChatRequest): AsyncGenerator<StreamEvent> {
    const system = req.messages.find((m) => m.role === "system")?.content;

    // tool_call id -> fonksiyon adı eşlemesi (Gemini functionResponse ad ister, id kullanmaz)
    const idToName = new Map<string, string>();
    for (const m of req.messages)
      for (const tc of m.toolCalls ?? []) idToName.set(tc.id, tc.name);

    const contents = req.messages
      .filter((m) => m.role !== "system")
      .map((m) => {
        if (m.role === "assistant" && m.toolCalls?.length) {
          const parts: unknown[] = [];
          if (m.content) parts.push({ text: m.content });
          for (const tc of m.toolCalls) parts.push({ functionCall: { name: tc.name, args: tc.args } });
          return { role: "model", parts };
        }
        if (m.role === "tool") {
          const name = m.toolCallId ? idToName.get(m.toolCallId) ?? "unknown" : "unknown";
          return {
            role: "user",
            parts: [{ functionResponse: { name, response: { result: parseMaybe(m.content) } } }],
          };
        }
        return { role: m.role === "assistant" ? "model" : "user", parts: [{ text: m.content }] };
      });

    const body: Record<string, unknown> = {
      contents,
      tools: [{ functionDeclarations: req.tools.map(toGeminiTool) }],
    };
    if (system) body.systemInstruction = { parts: [{ text: system }] };

    const url =
      `https://generativelanguage.googleapis.com/v1beta/models/${req.model}:streamGenerateContent` +
      `?alt=sse&key=${encodeURIComponent(req.apiKey)}`;

    const res = await fetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });

    if (!res.ok || !res.body) {
      yield { type: "error", message: `Gemini ${res.status}: ${await res.text()}` };
      return;
    }

    for await (const data of sseLines(res.body)) {
      let json: any;
      try {
        json = JSON.parse(data);
      } catch {
        continue;
      }
      const parts = json.candidates?.[0]?.content?.parts ?? [];
      for (const p of parts) {
        if (typeof p.text === "string") yield { type: "token", text: p.text };
        if (p.functionCall) {
          yield {
            type: "tool_call",
            id: "call_" + Math.random().toString(36).slice(2, 10),
            name: p.functionCall.name,
            args: (p.functionCall.args as Record<string, unknown>) ?? {},
          };
        }
      }
      if (json.usageMetadata) {
        yield {
          type: "usage",
          inputTokens: json.usageMetadata.promptTokenCount ?? 0,
          outputTokens: json.usageMetadata.candidatesTokenCount ?? 0,
        };
      }
    }
    yield { type: "done" };
  }
}

function toGeminiTool(t: ToolSchema) {
  return { name: t.name, description: t.description, parameters: sanitizeSchema(t.parameters) };
}

// Gemini, OpenAPI 3.0 şema alt kümesini ister: minItems/maxItems gibi alanları atar,
// type'ı olmayan alanlara "string" atar.
function sanitizeSchema(schema: any): any {
  if (schema === null || typeof schema !== "object") return schema;
  if (Array.isArray(schema)) return schema.map(sanitizeSchema);
  const out: any = {};
  for (const [k, v] of Object.entries(schema)) {
    if (k === "minItems" || k === "maxItems") continue;
    out[k] = sanitizeSchema(v);
  }
  if (out.properties && typeof out.properties === "object") {
    for (const key of Object.keys(out.properties)) {
      const prop = out.properties[key];
      if (prop && typeof prop === "object" && !prop.type && !prop.enum) prop.type = "string";
    }
  }
  return out;
}

function parseMaybe(s: string): unknown {
  try {
    return JSON.parse(s);
  } catch {
    return s;
  }
}
