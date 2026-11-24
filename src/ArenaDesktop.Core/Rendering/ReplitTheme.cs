namespace ArenaDesktop.Core.Rendering;

/// <summary>
/// Replit-style design tokens + stylesheet. Palette mirrors Replit's dark
/// workspace: deep navy surface (#0E1525), raised panel (#1C2333), hairline
/// border (#2B3245), off-white text (#F5F9FC), muted (#9DA8B9), orange accent
/// (#F26207), and Replit's editor syntax colours. Font stack matches Replit's
/// UI (Inter / IBM Plex Sans) with JetBrains Mono / Fira Code for code.
/// Self-contained: no remote fonts or scripts are ever fetched.
/// </summary>
public static class ReplitTheme
{
    public const string Background = "#0E1525";
    public const string Surface = "#1C2333";
    public const string SurfaceHover = "#252E44";
    public const string Border = "#2B3245";
    public const string Foreground = "#F5F9FC";
    public const string Muted = "#9DA8B9";
    public const string Accent = "#F26207";
    public const string Green = "#2ECC71";
    public const string Red = "#E5484D";
    public const string Blue = "#4C9AFF";
    public const string Purple = "#B392F0";
    public const string Yellow = "#E3B341";

    public const string Css = """
    :root{--bg:#0E1525;--surface:#1C2333;--hover:#252E44;--border:#2B3245;--fg:#F5F9FC;--muted:#9DA8B9;--accent:#F26207;--radius:8px}
    *{box-sizing:border-box} body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.6 "Inter","IBM Plex Sans",system-ui,sans-serif;padding:12px}
    .text{white-space:pre-wrap;word-wrap:break-word;margin:0 0 12px}
    .codeblock{background:var(--surface);border:1px solid var(--border);border-radius:var(--radius);margin:0 0 12px;overflow:hidden}
    .codeblock.streaming{border-color:var(--accent)}
    .cb-head{display:flex;align-items:center;gap:8px;padding:6px 8px 6px 12px;background:#171E2E;border-bottom:1px solid var(--border);font-size:12px}
    .cb-file{font-family:"JetBrains Mono","Fira Code",Consolas,monospace;color:var(--fg)}
    .cb-lang{color:var(--muted);text-transform:lowercase;background:var(--hover);border-radius:4px;padding:1px 6px}
    .cb-spacer{flex:1}
    .cb-btn{background:transparent;border:1px solid transparent;color:var(--muted);border-radius:6px;padding:4px 8px;cursor:pointer;font:inherit;font-size:12px}
    .cb-btn:hover{background:var(--hover);color:var(--fg)} .cb-btn:active{color:var(--accent)}
    .cb-body{display:flex;overflow-x:auto}
    .cb-gutter{padding:10px 8px 10px 12px;text-align:right;color:#5A6B84;user-select:none;font:13px/1.55 "JetBrains Mono",Consolas,monospace;border-right:1px solid var(--border)}
    pre.cb-code{margin:0;padding:10px 12px;font:13px/1.55 "JetBrains Mono","Fira Code",Consolas,monospace;flex:1}
    .tok-keyword{color:#FF7B72}.tok-string{color:#A5D6FF}.tok-comment{color:#8B949E;font-style:italic}
    .tok-number{color:#79C0FF}.tok-type{color:#FFA657}.tok-function{color:#D2A8FF}
    .tok-tag{color:#7EE787}.tok-attribute{color:#79C0FF}.tok-property{color:#79C0FF}.tok-punctuation{color:#C9D1D9}
    .cursor::after{content:"▋";color:var(--accent);animation:blink 1s steps(1) infinite}
    @keyframes blink{50%{opacity:0}}
    .widget{background:var(--surface);border:1px dashed var(--border);border-radius:var(--radius);padding:12px;margin:0 0 12px}
    .widget-head{font-size:12px;color:var(--muted);text-transform:uppercase;letter-spacing:.06em;margin-bottom:8px}
    .widget iframe{width:100%;border:0;border-radius:6px;background:#fff}
    table.w-table{border-collapse:collapse;width:100%} .w-table th,.w-table td{border:1px solid var(--border);padding:4px 8px;text-align:left}
    """;

    /// <summary>
    /// Bridge script: buttons post {type:"copy"|"run"|"expand", blockId, code}
    /// to the WPF host via window.chrome.webview.postMessage. No eval, no remote code.
    /// </summary>
    public const string BridgeJs = """
    document.addEventListener('click',e=>{const b=e.target.closest('[data-action]');if(!b)return;
    const blk=b.closest('[data-block]');const msg={type:b.dataset.action,blockId:blk?.dataset.block||'',
    code:blk?.querySelector('pre')?.innerText||''};
    if(b.dataset.action==='copy'){navigator.clipboard?.writeText(msg.code);b.textContent='Copied';setTimeout(()=>b.textContent='Copy',1200);return;}
    window.chrome?.webview?.postMessage(JSON.stringify(msg));});
    """;
}
