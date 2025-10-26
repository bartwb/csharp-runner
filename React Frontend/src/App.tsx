import { useState } from "react";
import Editor from "@monaco-editor/react";

type RunResponse = {
  stdout?: string;
  stderr?: string;
  exitCode?: number | null;
  result?: unknown; 
};

export default function App() {
  const [code, setCode] = useState<string>(
    `// Dit werkt nu!
using System;

Console.WriteLine("Hallo vanuit mijn EIGEN C# API! 👋");
Console.WriteLine($"Het antwoord is: {123 + 456}");

// Je kunt zelfs Console.Error.WriteLine gebruiken voor fouten
Console.Error.WriteLine("Dit is een test-foutmelding.");
`
  );

  const [stdout, setStdout] = useState<string>("");
  const [stderr, setStderr] = useState<string>("");
  const [exitCode, setExitCode] = useState<number | null>(null);
  const [httpStatus, setHttpStatus] = useState<number | null>(null);
  const [loading, setLoading] = useState<boolean>(false);
  const [durationMs, setDurationMs] = useState<number | null>(null);

  const handleRun = async () => {
    setLoading(true);
    setDurationMs(null);
    const t0 = performance.now();
    try {
      const resp = await fetch("http://127.0.0.1:50505/run", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          session_id: "web-ide-user-1",
          language: "csharp",
          code,
        }),
      });

      setHttpStatus(resp.status);

      const ct = (resp.headers.get("content-type") || "").toLowerCase();
      const raw = await resp.text();

      let data: RunResponse | null = null;
      if (ct.includes("application/json")) {
        try {
          data = JSON.parse(raw);
        } catch {
          data = null;
        }
      }

      if (data) {
        setStdout(data.stdout ?? "");
        setStderr(data.stderr ?? (resp.ok ? "" : raw));
        setExitCode(
          typeof data.exitCode === "number" ? data.exitCode : resp.ok ? 0 : null
        );
      } else {
         if (resp.ok) {
          setStdout(raw);
          setStderr("");
          setExitCode(0);
        } else {
          setStdout("");
          setStderr(raw || resp.statusText);
          setExitCode(null);
        }
      }
    } catch (e: any) {
      setHttpStatus(null);
      setStdout("");
      setStderr(`Client error: ${e?.message || e}`);
      setExitCode(null);
    } finally {
      setDurationMs(Math.round(performance.now() - t0));
      setLoading(false);
    }
  };

  const handleReset = () => setCode("");
  const handleClearOutput = () => {
    setStdout("");
    setStderr("");
    setExitCode(null);
    setHttpStatus(null);
    setDurationMs(null);
  };

  const copy = async (text: string) => {
    try {
      await navigator.clipboard.writeText(text);
    } catch {}
  };

  return (
    <div
      style={{
        height: "100vh",
        display: "grid",
        gridTemplateRows: "auto 1fr auto auto",
        gap: 8,
        padding: 16,
        background: "#0b0f14",
        color: "#eef2f7",
        fontFamily:
          "-apple-system,BlinkMacSystemFont,Segoe UI,Roboto,Helvetica,Arial,Apple Color Emoji,Segoe UI Emoji,sans-serif",
      }}
    >
      {/* Header */}
      <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
        <h1 style={{ margin: 0, fontSize: 20 }}>P.O.C. Web IDE (C#)</h1>
        <div style={{ opacity: 0.8, fontSize: 12 }}>
          {loading ? "Running…" : "Idle"}
          {durationMs != null && ` • ${durationMs} ms`}
          {httpStatus != null && ` • HTTP ${httpStatus}`}
          {exitCode != null && ` • exitCode ${exitCode}`}
        </div>
      </div>

      {/* Editor */}
      <div
        style={{
          border: "1px solid #1c2430",
          borderRadius: 12,
          overflow: "hidden",
          boxShadow: "0 0 0 1px rgba(255,255,255,0.02) inset",
        }}
      >
        <Editor
          height="60vh"
          language="csharp"
          value={code}
          onChange={(v) => setCode(v ?? "")}
          options={{
            fontSize: 14,
            minimap: { enabled: false },
            scrollBeyondLastLine: false,
            automaticLayout: true,
            theme: "vs-dark",
          }}
        />
      </div>

      {/* Controls */}
      <div style={{ display: "flex", gap: 8 }}>
        <button
          onClick={handleRun}
          disabled={loading}
          style={{
            padding: "10px 14px",
            borderRadius: 10,
            border: "1px solid #2a3442",
            background: loading ? "#1f2937" : "#111827",
            color: "#e5e7eb",
            cursor: loading ? "not-allowed" : "pointer",
          }}
        >
          {loading ? "Running…" : "Run"}
        </button>
        <button
          onClick={handleReset}
          disabled={loading}
          style={{
            padding: "10px 14px",
            borderRadius: 10,
            border: "1px solid #2a3442",
            background: "#0f172a",
            color: "#e5e7eb",
            cursor: loading ? "not-allowed" : "pointer",
          }}
        >
          Reset
        </button>
        <button
          onClick={handleClearOutput}
          disabled={loading}
          style={{
            padding: "10px 14px",
            borderRadius: 10,
            border: "1px solid #2a3442",
            background: "#0b1220",
            color: "#cbd5e1",
            cursor: loading ? "not-allowed" : "pointer",
          }}
        >
          Clear Output
        </button>
      </div>

      {/* Output console */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "1fr 1fr",
          gap: 12,
          alignItems: "start",
        }}
      >
        {/* STDOUT */}
        <div
          style={{
            border: "1px solid #1c2430",
            borderRadius: 12,
            overflow: "hidden",
            background: "#0c111b",
          }}
        >
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              gap: 8,
              padding: "8px 10px",
              borderBottom: "1px solid #1c2430",
              background: "#0e1523",
              fontSize: 12,
              color: "#a5b4fc",
            }}
          >
            <span>stdout</span>
            <button
              onClick={() => copy(stdout)}
              disabled={!stdout}
              style={{
                padding: "4px 8px",
                borderRadius: 8,
                border: "1px solid #2a3442",
                background: "#0b1220",
                color: "#cbd5e1",
                cursor: stdout ? "pointer" : "not-allowed",
              }}
              title="Copy stdout"
            >
              Copy
            </button>
          </div>
          <pre
            style={{
              margin: 0,
              padding: 12,
              minHeight: "18vh",
              maxHeight: "28vh",
              overflow: "auto",
              whiteSpace: "pre-wrap",
              wordBreak: "break-word",
              fontFamily: "ui-monospace, SFMono-Regular, Menlo, Monaco, monospace",
              fontSize: 13,
              color: "#e5e7eb",
            }}
          >
            {stdout || "—"}
          </pre>
        </div>

        {/* STDERR */}
        <div
          style={{
            border: "1px solid #1c2430",
            borderRadius: 12,
            overflow: "hidden",
            background: "#0c111b",
          }}
        >
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              gap: 8,
              padding: "8px 10px",
              borderBottom: "1px solid #1c2430",
              background: "#0e1523",
              fontSize: 12,
              color: "#fca5a5",
            }}
          >
            <span>stderr</span>
            <button
              onClick={() => copy(stderr)}
              disabled={!stderr}
              style={{
                padding: "4px 8px",
                borderRadius: 8,
                border: "1px solid #2a3442",
                background: "#0b1220",
                color: "#cbd5e1",
                cursor: stderr ? "pointer" : "not-allowed",
              }}
              title="Copy stderr"
            >
              Copy
            </button>
          </div>
          <pre
            style={{
              margin: 0,
              padding: 12,
              minHeight: "18vh",
              maxHeight: "28vh",
              overflow: "auto",
              whiteSpace: "pre-wrap",
              wordBreak: "break-word",
              fontFamily: "ui-monospace, SFMono-Regular, Menlo, Monaco, monospace",
              fontSize: 13,
              color: "#fecaca",
            }}
          >
            {stderr || "—"}
          </pre>
        </div>
      </div>
    </div>
  );
}
