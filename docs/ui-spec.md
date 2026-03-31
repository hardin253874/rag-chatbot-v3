# RAG Chatbot v3 -- UI Design Specification

**Version:** 1.0
**Date:** 2026-03-31
**Status:** Ready for implementation
**Target:** Next.js 14 / TypeScript / Tailwind CSS
**Viewport:** Desktop-first (min 1280px)

---

## 1. Design Overview

### Philosophy

The RAG Chatbot v3 interface follows **functional minimalism** -- every element earns its place by serving a clear purpose. The design takes cues from modern developer tools and SaaS dashboards (Linear, Vercel, Raycast) rather than generic chatbot templates. The result is an interface that feels like a professional internal tool, not a toy demo.

### Visual Direction

- **Dark sidebar, light content area** -- establishes visual hierarchy and draws focus to the chat
- **Muted, professional palette** -- slate grays with a single accent color (indigo) for interactive elements
- **Generous whitespace** -- content breathes; panels are not cramped
- **Subtle depth** -- shadows and borders create layering without heavy decoration
- **Monospace accents** -- source citations and log entries use monospace to signal "data" vs "UI"

### Key Principles

1. **Clarity over decoration** -- no gradients, no illustrations, no visual noise
2. **State visibility** -- the user always knows what is happening (loading, streaming, error, idle)
3. **Keyboard-first** -- all actions reachable without a mouse
4. **Progressive disclosure** -- secondary actions (clear KB, list resources) are visually subdued until needed

---

## 2. Color System

### Primary Palette

| Role | Hex | Tailwind Class | Usage |
|------|-----|----------------|-------|
| Accent | `#6366F1` | `indigo-500` | Primary buttons, active states, links |
| Accent Hover | `#4F46E5` | `indigo-600` | Button hover, active link hover |
| Accent Light | `#EEF2FF` | `indigo-50` | User message bubble background |
| Accent Subtle | `#E0E7FF` | `indigo-100` | User message bubble hover, focus rings |
| Accent Text | `#4338CA` | `indigo-700` | Accent text on light backgrounds |

### Neutral Palette

| Role | Hex | Tailwind Class | Usage |
|------|-----|----------------|-------|
| Sidebar BG | `#0F172A` | `slate-900` | Sidebar background |
| Sidebar BG Alt | `#1E293B` | `slate-800` | Sidebar section backgrounds, input fields in sidebar |
| Sidebar Border | `#334155` | `slate-700` | Dividers and borders within sidebar |
| Sidebar Text | `#F8FAFC` | `slate-50` | Primary text in sidebar |
| Sidebar Text Muted | `#94A3B8` | `slate-400` | Secondary/label text in sidebar |
| Header BG | `#FFFFFF` | `white` | Header background |
| Header Border | `#E2E8F0` | `slate-200` | Header bottom border |
| Content BG | `#F8FAFC` | `slate-50` | Chat area background |
| Surface | `#FFFFFF` | `white` | Cards, message bubbles, inputs |
| Border | `#E2E8F0` | `slate-200` | General borders in light areas |
| Border Subtle | `#F1F5F9` | `slate-100` | Very subtle dividers |
| Text Primary | `#0F172A` | `slate-900` | Headings, body text in light areas |
| Text Secondary | `#475569` | `slate-600` | Secondary text, labels in light areas |
| Text Muted | `#94A3B8` | `slate-400` | Placeholders, disabled text |

### Semantic Colors

| Role | Hex | Tailwind Class | Usage |
|------|-----|----------------|-------|
| Success | `#22C55E` | `green-500` | Success log entries, status dot |
| Success BG | `#F0FDF4` | `green-50` | Success notification backgrounds |
| Success Text | `#15803D` | `green-700` | Success text in light areas |
| Error | `#EF4444` | `red-500` | Error log entries, danger buttons |
| Error BG | `#FEF2F2` | `red-50` | Error notification backgrounds |
| Error Text | `#B91C1C` | `red-700` | Error text in light areas |
| Error Hover | `#DC2626` | `red-600` | Danger button hover |
| Info | `#3B82F6` | `blue-500` | Info log entries |
| Info BG | `#EFF6FF` | `blue-50` | Info notification backgrounds |
| Info Text | `#1D4ED8` | `blue-700` | Info text in light areas |
| Warning | `#F59E0B` | `amber-500` | Warning states |
| Warning BG | `#FFFBEB` | `amber-50` | Warning backgrounds |

### Status Indicator Colors

| State | Dot Color | Tailwind | Label |
|-------|-----------|----------|-------|
| Connected | `#22C55E` | `bg-green-500` | "Connected" |
| Disconnected | `#EF4444` | `bg-red-500` | "Disconnected" |
| Connecting | `#F59E0B` | `bg-amber-500` | "Connecting..." |

---

## 3. Typography

### Font Stack

| Purpose | Font Family | Tailwind Class |
|---------|-------------|----------------|
| UI Text | `Inter, system-ui, -apple-system, sans-serif` | `font-sans` (configure in `tailwind.config.ts`) |
| Monospace | `JetBrains Mono, Menlo, Monaco, Consolas, monospace` | `font-mono` (configure in `tailwind.config.ts`) |

Load Inter and JetBrains Mono via `next/font/google` for optimal performance.

### Type Scale

| Token | Size | Line Height | Weight | Tailwind Classes | Usage |
|-------|------|-------------|--------|------------------|-------|
| display | 20px | 28px | 600 | `text-xl font-semibold leading-7` | App name in header |
| heading-sm | 13px | 18px | 600 | `text-xs font-semibold leading-[18px] uppercase tracking-wider` | Section labels ("SETTINGS", "KNOWLEDGE BASE") |
| body | 14px | 20px | 400 | `text-sm leading-5` | Default body text, messages, inputs |
| body-medium | 14px | 20px | 500 | `text-sm font-medium leading-5` | Button labels, active items |
| caption | 12px | 16px | 400 | `text-xs leading-4` | Timestamps, metadata, log entries |
| caption-medium | 12px | 16px | 500 | `text-xs font-medium leading-4` | Badge labels, small buttons |
| mono | 12px | 16px | 400 | `text-xs font-mono leading-4` | Source citations, log entries, LLM info |

---

## 4. Layout System

### Core Dimensions

| Element | Value | Tailwind |
|---------|-------|----------|
| Sidebar width | 320px | `w-80` |
| Header height | 56px | `h-14` |
| Chat input bar height | 72px | `h-[72px]` |
| Max chat content width | 768px | `max-w-3xl` |
| Message max width | 85% of chat area | `max-w-[85%]` |

### Spacing Scale

Use Tailwind's default spacing scale consistently:

| Token | Value | Tailwind | Usage |
|-------|-------|----------|-------|
| xs | 4px | `1` | Inline spacing, icon gaps |
| sm | 8px | `2` | Tight padding, small gaps |
| md | 12px | `3` | Input padding, card padding |
| base | 16px | `4` | Section gaps, standard padding |
| lg | 20px | `5` | Panel padding |
| xl | 24px | `6` | Major section spacing |
| 2xl | 32px | `8` | Large gaps between sections |

### Border Radius

| Token | Value | Tailwind | Usage |
|-------|-------|----------|-------|
| sm | 6px | `rounded-md` | Buttons, inputs, badges |
| base | 8px | `rounded-lg` | Cards, message bubbles |
| lg | 12px | `rounded-xl` | Dialogs, large cards |
| full | 9999px | `rounded-full` | Status dots, avatars |

### Shadow System

| Token | Value | Tailwind | Usage |
|-------|-------|----------|-------|
| sm | `0 1px 2px rgba(0,0,0,0.05)` | `shadow-sm` | Inputs, subtle elevation |
| base | `0 1px 3px rgba(0,0,0,0.1), 0 1px 2px rgba(0,0,0,0.06)` | `shadow` | Bot message bubbles, cards |
| md | `0 4px 6px rgba(0,0,0,0.07), 0 2px 4px rgba(0,0,0,0.06)` | `shadow-md` | Dropdowns, hovering elements |
| lg | `0 10px 15px rgba(0,0,0,0.1), 0 4px 6px rgba(0,0,0,0.05)` | `shadow-lg` | Modals, dialogs |
| xl | `0 20px 25px rgba(0,0,0,0.1), 0 8px 10px rgba(0,0,0,0.04)` | `shadow-xl` | Dialog overlay |

---

## 5. Component Specifications

### 5.1 Header

**Layout:** Fixed top bar, full width, flex row with items centered vertically.

```
Classes: "h-14 bg-white border-b border-slate-200 px-6 flex items-center justify-between"
```

**Left section (app identity):**
```
Container: "flex items-center gap-3"
App name: "text-xl font-semibold text-slate-900 leading-7"
  Content: "RAG Chatbot"
Subtitle: "text-sm text-slate-400 leading-5"
  Content: "v3"
```

**Right section (connection status):**
```
Container: "flex items-center gap-2"
Status dot: "w-2 h-2 rounded-full"
  Connected:    "bg-green-500"
  Disconnected: "bg-red-500"
  Connecting:   "bg-amber-500 animate-pulse"
Status label: "text-xs text-slate-500 leading-4"
  Content: "Connected" | "Disconnected" | "Connecting..."
```

### 5.2 Sidebar

**Layout:** Fixed left column, full height below header, vertical flex.

```
Container: "w-80 bg-slate-900 border-r border-slate-800 flex flex-col h-[calc(100vh-56px)] overflow-hidden"
```

The sidebar contains two main sections stacked vertically:
1. Settings section (fixed height, does not scroll)
2. Knowledge Base panel (fills remaining space, scrolls internally)

**Section divider between settings and KB:**
```
"border-t border-slate-700"
```

### 5.3 Settings Section

**Container:**
```
"px-5 py-4 flex-shrink-0"
```

**Section label:**
```
"text-xs font-semibold uppercase tracking-wider text-slate-400 mb-3"
Content: "SETTINGS"
```

**LLM Info Display:**

A read-only info block showing the configured LLM model and base URL fetched from `GET /config`.

```
Container: "bg-slate-800 rounded-lg p-3 space-y-2"
Label:     "text-xs font-semibold uppercase tracking-wider text-slate-400"
           Content: "LLM"
Model row:
  Container: "flex items-center justify-between"
  Label:     "text-xs text-slate-400"  Content: "Model"
  Value:     "text-xs font-mono text-slate-50"  Content: e.g. "gpt-4o-mini"
Base URL row:
  Container: "flex items-center justify-between"
  Label:     "text-xs text-slate-400"  Content: "Base URL"
  Value:     "text-xs font-mono text-slate-50 truncate max-w-[180px]"  Content: e.g. "https://api.openai.com"
```

**Loading state (while fetching /config):**
```
Model value: "text-xs font-mono text-slate-500 animate-pulse"  Content: "Loading..."
Base URL value: same treatment
```

**Error state (if /config fetch fails):**
```
Model value: "text-xs font-mono text-red-400"  Content: "Unavailable"
```

### 5.4 Knowledge Base Panel

**Container:**
```
"flex-1 flex flex-col overflow-hidden px-5 py-4"
```

**Section label:**
```
"text-xs font-semibold uppercase tracking-wider text-slate-400 mb-3 flex-shrink-0"
Content: "KNOWLEDGE BASE"
```

**Scrollable content area:**
```
"flex-1 overflow-y-auto space-y-4 scrollbar-thin scrollbar-thumb-slate-700 scrollbar-track-transparent"
```

#### 5.4.1 URL Input Group

```
Container: "space-y-2"
Label:     "text-xs font-medium text-slate-300"  Content: "Add URL"
Input:     (See Section 5.11 -- Sidebar Input variant)
           Placeholder: "https://example.com/page"
Button:    (See Section 5.10 -- Secondary Button, sidebar variant)
           Content: "Add URL"
           Full width: "w-full"
```

#### 5.4.2 File Upload Group

```
Container: "space-y-2"
Label:     "text-xs font-medium text-slate-300"  Content: "Upload File"
```

**Custom file input (styled to match dark sidebar):**

The native file input is hidden. A custom button-like element triggers it.

```
Hidden input: "sr-only" with accept=".md,.txt"
Visible trigger:
  Container: "flex items-center gap-2 w-full"
  File label area:
    "flex-1 bg-slate-800 border border-slate-700 rounded-md px-3 py-2 text-xs text-slate-400 truncate
     cursor-pointer hover:border-slate-600 transition-colors duration-150"
    Content (no file): "No file chosen"
    Content (file selected): filename, e.g. "readme.md"
    Content (file selected) text color: "text-slate-200"
Upload button: (See Section 5.10 -- Secondary Button, sidebar variant)
  Content: "Upload"
  Disabled when no file selected
```

#### 5.4.3 Activity Log

```
Container: "space-y-1"
Label:     "text-xs font-medium text-slate-300 mb-1"  Content: "Activity"
Log area:  "bg-slate-800/50 rounded-lg p-3 max-h-40 overflow-y-auto space-y-1
            scrollbar-thin scrollbar-thumb-slate-700 scrollbar-track-transparent"
```

**Log entries:**

Each entry is a single line with a colored bullet.

```
Entry container: "flex items-start gap-2 text-xs leading-4"
Bullet: "w-1.5 h-1.5 rounded-full mt-[5px] flex-shrink-0"
Text:   "font-mono break-words"
```

| Type | Bullet Class | Text Class |
|------|-------------|------------|
| Info | `bg-blue-400` | `text-blue-300` |
| Success | `bg-green-400` | `text-green-300` |
| Error | `bg-red-400` | `text-red-300` |

**Initial state:** Single info entry: "Ready. Add documents to get started."

#### 5.4.4 Resource List

```
Toggle button: (See Section 5.10 -- Ghost Button, sidebar variant)
  Content: "List Resources"
  Icon: ChevronDown (rotates when expanded)
```

**Expanded resource list:**
```
Container: "bg-slate-800/50 rounded-lg p-3 max-h-32 overflow-y-auto mt-2
            scrollbar-thin scrollbar-thumb-slate-700 scrollbar-track-transparent"
Resource item: "text-xs font-mono text-slate-300 py-0.5 truncate"
Empty state:   "text-xs text-slate-500 italic"  Content: "No resources found."
Loading state: "text-xs text-slate-400 animate-pulse"  Content: "Loading..."
```

#### 5.4.5 Clear Knowledge Base Button

```
(See Section 5.10 -- Danger Button, sidebar variant)
Content: "Clear Knowledge Base"
Full width: "w-full"
Positioned at bottom of scrollable area with top margin: "mt-4"
```

### 5.5 Chat Area

**Container:**
```
"flex-1 flex flex-col bg-slate-50 h-[calc(100vh-56px)]"
```

**Inner structure (two children):**
1. Message area (flex-1, scrollable)
2. Input bar (fixed height at bottom)

### 5.6 Message Area

```
Container: "flex-1 overflow-y-auto px-6 py-6"
Inner:     "max-w-3xl mx-auto space-y-4"
```

Auto-scroll behavior: scroll to bottom when new content arrives, but only if the user was already at the bottom (within 100px threshold). If the user has scrolled up to read history, do not force scroll.

### 5.7 Empty State

Displayed when no messages exist.

```
Container: "flex flex-col items-center justify-center h-full text-center"
Icon:      "w-12 h-12 text-slate-300 mb-4"
           Use a chat/message-square icon (from lucide-react)
Heading:   "text-sm font-medium text-slate-400 mb-1"
           Content: "Ask a question about your documents"
Subtext:   "text-xs text-slate-400"
           Content: "Upload documents to the knowledge base, then start a conversation."
```

### 5.8 Message Bubbles

#### User Message

```
Row:     "flex justify-end"
Bubble:  "max-w-[85%] bg-indigo-50 text-slate-900 rounded-lg px-4 py-2.5
          text-sm leading-5"
```

No shadow on user messages. The indigo-50 background is enough visual distinction.

#### Bot Message

```
Row:     "flex justify-start"
Wrapper: "max-w-[85%] space-y-2"
Bubble:  "bg-white text-slate-900 rounded-lg px-4 py-2.5 shadow
          text-sm leading-5"
```

Bot message text should render markdown. Use a lightweight markdown renderer (e.g., `react-markdown`) with the following overrides:

```
Paragraphs: "mb-2 last:mb-0"
Code inline: "bg-slate-100 text-indigo-700 px-1 py-0.5 rounded text-xs font-mono"
Code block: "bg-slate-900 text-slate-100 p-3 rounded-md text-xs font-mono overflow-x-auto my-2"
Lists: "list-disc pl-4 mb-2 space-y-1"
Bold: "font-semibold"
Links: "text-indigo-600 underline hover:text-indigo-700"
```

#### Error Message (bot role, error type)

```
Bubble: "bg-red-50 text-red-700 border border-red-200 rounded-lg px-4 py-2.5
         text-sm leading-5"
```

### 5.9 Source Citations

Displayed below the bot message bubble when sources are present.

```
Container: "flex flex-wrap items-center gap-1.5 px-1"
Label:     "text-xs text-slate-400"  Content: "Sources:"
```

**Individual source link:**
```
"inline-flex items-center gap-1 text-xs font-mono text-indigo-500
 hover:text-indigo-600 hover:underline transition-colors duration-150
 bg-indigo-50 px-2 py-0.5 rounded-md cursor-pointer"
```

For URL sources, clicking opens in a new tab. For file sources, display only (no navigation).

Icon before text: a small document or link icon (12x12) from lucide-react.

### 5.10 Buttons

#### Primary Button

| State | Classes |
|-------|---------|
| Default | `bg-indigo-500 text-white text-sm font-medium px-4 py-2 rounded-md shadow-sm transition-all duration-150` |
| Hover | `hover:bg-indigo-600 hover:shadow` |
| Active | `active:bg-indigo-700 active:shadow-sm` |
| Focus | `focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 focus-visible:ring-offset-2` |
| Disabled | `disabled:bg-indigo-300 disabled:cursor-not-allowed disabled:shadow-none` |

#### Secondary Button (Light Context)

| State | Classes |
|-------|---------|
| Default | `bg-white text-slate-700 text-sm font-medium px-4 py-2 rounded-md border border-slate-300 shadow-sm transition-all duration-150` |
| Hover | `hover:bg-slate-50 hover:border-slate-400` |
| Active | `active:bg-slate-100` |
| Focus | `focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 focus-visible:ring-offset-2` |
| Disabled | `disabled:bg-slate-50 disabled:text-slate-400 disabled:border-slate-200 disabled:cursor-not-allowed` |

#### Secondary Button (Sidebar/Dark Context)

| State | Classes |
|-------|---------|
| Default | `bg-slate-800 text-slate-200 text-xs font-medium px-3 py-1.5 rounded-md border border-slate-700 transition-all duration-150` |
| Hover | `hover:bg-slate-700 hover:border-slate-600 hover:text-white` |
| Active | `active:bg-slate-600` |
| Focus | `focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 focus-visible:ring-offset-2 focus-visible:ring-offset-slate-900` |
| Disabled | `disabled:bg-slate-800/50 disabled:text-slate-600 disabled:border-slate-700/50 disabled:cursor-not-allowed` |

#### Ghost Button (Sidebar)

Used for "List Resources" toggle.

| State | Classes |
|-------|---------|
| Default | `text-slate-400 text-xs font-medium px-2 py-1 rounded-md transition-all duration-150` |
| Hover | `hover:text-slate-200 hover:bg-slate-800` |
| Active | `active:bg-slate-700` |
| Focus | `focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 focus-visible:ring-offset-2 focus-visible:ring-offset-slate-900` |

#### Danger Button (Sidebar)

| State | Classes |
|-------|---------|
| Default | `bg-transparent text-red-400 text-xs font-medium px-3 py-1.5 rounded-md border border-red-500/30 transition-all duration-150` |
| Hover | `hover:bg-red-500/10 hover:border-red-500/50 hover:text-red-300` |
| Active | `active:bg-red-500/20` |
| Focus | `focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-red-500 focus-visible:ring-offset-2 focus-visible:ring-offset-slate-900` |

#### Send Button (Chat Input)

A square button with an icon (arrow-up or send icon from lucide-react).

| State | Classes |
|-------|---------|
| Default | `bg-indigo-500 text-white w-9 h-9 rounded-md flex items-center justify-center shadow-sm transition-all duration-150` |
| Hover | `hover:bg-indigo-600` |
| Active | `active:bg-indigo-700` |
| Focus | `focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 focus-visible:ring-offset-2` |
| Disabled | `disabled:bg-slate-200 disabled:text-slate-400 disabled:cursor-not-allowed disabled:shadow-none` |

Icon size: `w-4 h-4`.

### 5.11 Inputs

#### Chat Text Input

```
Container: "flex-1"
Input: "w-full bg-white border border-slate-300 rounded-md px-4 py-2.5
        text-sm text-slate-900 placeholder:text-slate-400
        transition-colors duration-150
        hover:border-slate-400
        focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500
        disabled:bg-slate-100 disabled:text-slate-400 disabled:cursor-not-allowed"
Placeholder: "Ask a question..."
```

#### Sidebar Text Input (URL input)

```
"w-full bg-slate-800 border border-slate-700 rounded-md px-3 py-2
 text-sm text-slate-100 placeholder:text-slate-500
 transition-colors duration-150
 hover:border-slate-600
 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500
 disabled:bg-slate-800/50 disabled:text-slate-600 disabled:cursor-not-allowed"
```

### 5.12 Thinking Indicator

Displayed as a bot message bubble while waiting for the first `chunk` event.

```
Row:    "flex justify-start"
Bubble: "bg-white rounded-lg px-4 py-3 shadow inline-flex items-center gap-1.5"
```

**Three animated dots:**

```
Dot: "w-1.5 h-1.5 rounded-full bg-slate-400"
```

Animation: Each dot scales up and down with a staggered delay, creating a wave effect.

```css
@keyframes thinking-bounce {
  0%, 60%, 100% {
    transform: translateY(0);
    opacity: 0.4;
  }
  30% {
    transform: translateY(-4px);
    opacity: 1;
  }
}

.thinking-dot {
  animation: thinking-bounce 1.4s ease-in-out infinite;
}

.thinking-dot:nth-child(1) { animation-delay: 0ms; }
.thinking-dot:nth-child(2) { animation-delay: 200ms; }
.thinking-dot:nth-child(3) { animation-delay: 400ms; }
```

Tailwind implementation: Define the keyframes in `tailwind.config.ts`:

```ts
// tailwind.config.ts
theme: {
  extend: {
    keyframes: {
      'thinking-bounce': {
        '0%, 60%, 100%': { transform: 'translateY(0)', opacity: '0.4' },
        '30%': { transform: 'translateY(-4px)', opacity: '1' },
      },
    },
    animation: {
      'thinking-bounce': 'thinking-bounce 1.4s ease-in-out infinite',
    },
  },
}
```

Apply via utility classes with inline `animation-delay` styles:

```
Dot 1: "w-1.5 h-1.5 rounded-full bg-slate-400 animate-thinking-bounce"
Dot 2: "w-1.5 h-1.5 rounded-full bg-slate-400 animate-thinking-bounce" style="animation-delay: 200ms"
Dot 3: "w-1.5 h-1.5 rounded-full bg-slate-400 animate-thinking-bounce" style="animation-delay: 400ms"
```

### 5.13 Confirmation Dialog

Used when clicking "Clear Knowledge Base".

**Overlay:**
```
"fixed inset-0 bg-black/50 z-50 flex items-center justify-center
 backdrop-blur-sm"
```

**Dialog panel:**
```
"bg-white rounded-xl shadow-xl w-full max-w-sm mx-4 p-6"
```

**Content:**
```
Icon:     "w-10 h-10 mx-auto mb-4 text-red-500"
          Use AlertTriangle icon from lucide-react
Title:    "text-base font-semibold text-slate-900 text-center mb-2"
          Content: "Clear Knowledge Base?"
Message:  "text-sm text-slate-600 text-center mb-6"
          Content: "This will permanently delete all documents and embeddings. Chat history will also be cleared. This action cannot be undone."
```

**Actions:**
```
Container: "flex gap-3"
Cancel button:
  "flex-1 bg-white text-slate-700 text-sm font-medium px-4 py-2.5 rounded-md
   border border-slate-300 hover:bg-slate-50 transition-colors duration-150
   focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 focus-visible:ring-offset-2"
  Content: "Cancel"
Confirm button:
  "flex-1 bg-red-500 text-white text-sm font-medium px-4 py-2.5 rounded-md
   hover:bg-red-600 active:bg-red-700 transition-colors duration-150
   focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-red-500 focus-visible:ring-offset-2"
  Content: "Yes, Clear Everything"
```

**Animation:** Dialog enters with a subtle scale-up and fade-in:
```css
/* Overlay */
transition: opacity 150ms ease-out;

/* Dialog panel */
transition: opacity 150ms ease-out, transform 150ms ease-out;
/* Enter from: */ opacity: 0; transform: scale(0.95);
/* Enter to: */   opacity: 1; transform: scale(1);
```

---

## 6. Interaction Design

### 6.1 Transitions & Animations

All interactive elements use consistent transition timing:

| Property | Duration | Easing | Tailwind |
|----------|----------|--------|----------|
| Color changes | 150ms | ease-out | `transition-colors duration-150` |
| All properties | 150ms | ease-out | `transition-all duration-150` |
| Dialog enter | 150ms | ease-out | Custom (see dialog spec) |
| Dialog exit | 100ms | ease-in | Custom |
| Thinking dots | 1400ms | ease-in-out | Custom keyframes |
| Status pulse | 2000ms | ease-in-out | `animate-pulse` (Tailwind built-in) |
| Chevron rotate | 200ms | ease-out | `transition-transform duration-200` |

### 6.2 Micro-Interactions

**Button press feedback:** All buttons use `active:` state to provide immediate tactile feedback (darker background, reduced shadow).

**Input focus:** Inputs transition from a subtle border to a prominent indigo ring (`ring-2 ring-indigo-500`) on focus, providing clear focus indication.

**Resource list chevron:** The chevron icon beside "List Resources" rotates 180 degrees when expanded:
```
Collapsed: "transform rotate-0 transition-transform duration-200"
Expanded:  "transform rotate-180 transition-transform duration-200"
```

**Message appearance:** New messages fade in from below:
```css
@keyframes message-enter {
  from {
    opacity: 0;
    transform: translateY(8px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}
```
Duration: 200ms, easing: ease-out.

In Tailwind config:
```ts
keyframes: {
  'message-enter': {
    from: { opacity: '0', transform: 'translateY(8px)' },
    to: { opacity: '1', transform: 'translateY(0)' },
  },
},
animation: {
  'message-enter': 'message-enter 200ms ease-out',
},
```

**Activity log entry appearance:** New log entries fade in:
```
animation: "animate-message-enter" (reuse the same keyframe, 150ms duration variant)
```

### 6.3 Loading States

**Ingestion loading (URL or File upload):**
- The button text changes to "Adding..." or "Uploading..."
- The button becomes disabled (uses disabled styles)
- A log entry of type "info" is added: "Ingesting {filename or URL}..."

**Chat streaming:**
- Input and send button become disabled
- Thinking indicator appears as a bot message
- When first chunk arrives, thinking indicator is replaced with the actual bot message bubble
- Text streams into the bubble character by character (appended as received)
- After `done` event, input re-enables and receives focus

**Config loading:**
- LLM info shows "Loading..." in muted pulsing text
- After load, values snap in (no animation needed)

### 6.4 Scroll Behavior

**Chat area:**
- `scroll-behavior: smooth` for programmatic scrolling
- Auto-scroll to bottom on new content only if user is at bottom (within 100px)
- If user scrolls up, a "scroll to bottom" indicator could appear (optional enhancement)

**Activity log and resource list:**
- `overflow-y: auto` with thin custom scrollbar
- New entries added at bottom, auto-scroll to show latest

---

## 7. States & Feedback

### 7.1 Empty States

| Context | Display |
|---------|---------|
| Chat area (no messages) | Centered icon + "Ask a question about your documents" + subtitle |
| Resource list (no resources) | "No resources found." in italic muted text |
| Activity log (initial) | Single info entry: "Ready. Add documents to get started." |

### 7.2 Loading States

| Context | Display |
|---------|---------|
| Config fetch | "Loading..." with pulse animation in LLM info area |
| Ingestion in progress | Button disabled + "Adding..." / "Uploading..." + info log entry |
| Chat waiting for response | Thinking indicator (3 bouncing dots in bot bubble) |
| Resource list loading | "Loading..." with pulse animation |

### 7.3 Error States

| Context | Display |
|---------|---------|
| Config fetch failure | LLM info shows "Unavailable" in red text |
| Ingestion failure | Error log entry (red): "Failed to ingest {source}: {error message}" |
| Chat error | Error-styled bot message (red-50 bg, red-700 text) with error description |
| Network error | Status indicator turns red ("Disconnected") |

### 7.4 Success States

| Context | Display |
|---------|---------|
| Ingestion complete | Success log entry (green): "Successfully ingested {source}" |
| KB cleared | Success log entry (green): "Knowledge base cleared" + chat history reset |

---

## 8. Accessibility

### 8.1 ARIA Attributes

| Element | ARIA |
|---------|------|
| Sidebar | `role="complementary"` `aria-label="Sidebar"` |
| Chat message area | `role="log"` `aria-label="Chat messages"` `aria-live="polite"` |
| Chat input | `aria-label="Message input"` |
| Send button | `aria-label="Send message"` |
| Thinking indicator | `role="status"` `aria-label="Generating response"` |
| Activity log | `role="log"` `aria-label="Activity log"` `aria-live="polite"` |
| Status indicator | `role="status"` `aria-label="Connection status: {state}"` |
| Confirmation dialog | `role="alertdialog"` `aria-labelledby="dialog-title"` `aria-describedby="dialog-description"` |
| Dialog overlay | `aria-hidden="true"` (overlay itself) |
| URL input | `aria-label="URL to ingest"` |
| File input | `aria-label="Choose file to upload"` |
| Clear KB button | `aria-label="Clear knowledge base"` |
| List Resources button | `aria-expanded="{true|false}"` `aria-controls="resource-list"` |
| Resource list | `id="resource-list"` `role="list"` |
| Each resource | `role="listitem"` |
| User message | `aria-label="You said"` |
| Bot message | `aria-label="Assistant response"` |

### 8.2 Keyboard Navigation

| Key | Context | Action |
|-----|---------|--------|
| `Enter` | Chat input | Send message (when not empty and not streaming) |
| `Escape` | Confirmation dialog | Close dialog (same as Cancel) |
| `Tab` | Global | Move focus through interactive elements in logical order |
| `Shift+Tab` | Global | Move focus backwards |
| `Enter` / `Space` | Any button | Activate button |

**Focus order:** Header status -> Sidebar (Settings -> KB URL input -> Add URL -> File input -> Upload -> List Resources -> Clear KB) -> Chat input -> Send button.

### 8.3 Focus Management

- When the confirmation dialog opens, focus moves to the Cancel button
- When the dialog closes, focus returns to the Clear KB button that triggered it
- After streaming completes, focus returns to the chat input
- After ingestion completes, focus remains on the button that was clicked (Add URL or Upload)
- Focus trap inside the confirmation dialog (Tab cycles between Cancel and Confirm only)

### 8.4 Color Contrast

All text meets WCAG AA contrast ratios (4.5:1 for normal text, 3:1 for large text):

| Combination | Ratio | Passes |
|-------------|-------|--------|
| `slate-900` on `white` | 15.4:1 | AA, AAA |
| `slate-600` on `white` | 5.7:1 | AA |
| `slate-400` on `white` | 3.5:1 | AA Large only -- used only for placeholders and decorative text |
| `slate-50` on `slate-900` | 15.4:1 | AA, AAA |
| `slate-400` on `slate-900` | 4.6:1 | AA |
| `slate-300` on `slate-900` | 7.8:1 | AA, AAA |
| `indigo-500` on `white` | 4.6:1 | AA |
| `red-700` on `red-50` | 7.1:1 | AA, AAA |
| `green-700` on `green-50` | 5.8:1 | AA |
| `blue-700` on `blue-50` | 6.3:1 | AA |
| `red-400` on `slate-900` | 4.8:1 | AA |
| `green-300` on `slate-900` | 8.2:1 | AA, AAA |
| `blue-300` on `slate-900` | 6.9:1 | AA |

### 8.5 Screen Reader Considerations

- Streaming text: The chat message area uses `aria-live="polite"` so screen readers announce new messages without interrupting. Individual streaming chunks are not announced -- only the completed message.
- Activity log: Uses `aria-live="polite"` to announce new entries.
- Status changes: The connection status uses `role="status"` for automatic announcements.

---

## 9. Page Layout Wireframe

```
                              1280px (min viewport)
 <--------------------------------------------------------------------------->
 |                                                                           |
 |  56px  +-----------------------------------------------------------------+|
 |   h    | RAG Chatbot  v3                           * Connected           ||
 |        +-----------------------------------------------------------------+|
 |        |             |                                                    ||
 |        |  SETTINGS   |                                                    ||
 |        |             |                                                    ||
 |        |  LLM        |                                                    ||
 |        |  Model: ... |           (empty state)                            ||
 |        |  URL:   ... |                                                    ||
 |        |             |        [chat icon]                                 ||
 |        |-------------| "Ask a question about your documents"              ||
 |        |             | "Upload documents to the knowledge base..."        ||
 |  100vh |  KNOWLEDGE  |                                                    ||
 |  -56px |  BASE       |                                                    ||
 |        |             |                                                    ||
 |        |  [URL input]|                                                    ||
 |        |  [Add URL]  |                                                    ||
 |        |             |                                                    ||
 |        |  [file..][ ]|                                                    ||
 |        |  [Upload]   |                                                    ||
 |        |             |                                                    ||
 |        |  Activity   |                                                    ||
 |        |  * Ready... |                                                    ||
 |        |             |                                                    ||
 |        |  > List Res.|                                                    ||
 |        |             |                                                    ||
 |        |  [Clear KB] |  +-----------------------------------------------+||
 |        |             |  | [Ask a question...                    ] [Send] |||
 |  72px  |             |  +-----------------------------------------------+||
 |        +-------------+---------------------------------------------------+|
 |                                                                           |
 <--------------------------------------------------------------------------->
          |<-- 320px -->|<-------------- remaining width ------------------>|


 Chat area with messages:

         +---------------------------------------------------+
         |                                                   |
         |                        +------------------------+ |
         |                        | User message here      | |
         |                        +------------------------+ |
         |                                                   |
         |  +-----------------------------+                  |
         |  | Bot response here with      |                  |
         |  | markdown **formatting**.    |                  |
         |  +-----------------------------+                  |
         |  Sources: [doc1.md] [doc2.txt]                    |
         |                                                   |
         |                        +------------------------+ |
         |                        | Another user question  | |
         |                        +------------------------+ |
         |                                                   |
         |  +--+                                             |
         |  |..|  <- thinking indicator                      |
         |  +--+                                             |
         |                                                   |
         +---------------------------------------------------+
         | [Ask a question...                       ] [>]    |
         +---------------------------------------------------+


 Input bar detail (72px height):

         +---------------------------------------------------+
         |   px-6 py-4                                       |
         |   +------------------------------------------+ +--+
         |   | Ask a question...                        | |> ||
         |   +------------------------------------------+ +--+
         |       ^                                        ^   |
         |     flex-1, rounded-md                    w-9 h-9  |
         +---------------------------------------------------+
              gap-3 between input and button


 Confirmation dialog:

         +------------------------------------------+
         |                                          |
         |        /!\  (AlertTriangle icon)          |
         |                                          |
         |      Clear Knowledge Base?                |
         |                                          |
         |  This will permanently delete all         |
         |  documents and embeddings. Chat history   |
         |  will also be cleared. This action        |
         |  cannot be undone.                        |
         |                                          |
         |  +----------+  +---------------------+   |
         |  |  Cancel   |  |Yes, Clear Everything|   |
         |  +----------+  +---------------------+   |
         |                                          |
         +------------------------------------------+
              max-w-sm, rounded-xl, shadow-xl
```

### Input Bar Layout

```
Container: "px-6 py-4 border-t border-slate-200 bg-white"
Inner:     "max-w-3xl mx-auto flex items-center gap-3"
```

The input bar sits at the bottom of the chat area, anchored and never scrolls. It has a top border to visually separate it from the message area.

---

## 10. Tailwind Configuration

The following customizations should be added to `tailwind.config.ts`:

```ts
import type { Config } from 'tailwindcss';

const config: Config = {
  content: [
    './src/**/*.{js,ts,jsx,tsx,mdx}',
  ],
  theme: {
    extend: {
      fontFamily: {
        sans: ['var(--font-inter)', 'system-ui', '-apple-system', 'sans-serif'],
        mono: ['var(--font-jetbrains-mono)', 'Menlo', 'Monaco', 'Consolas', 'monospace'],
      },
      keyframes: {
        'thinking-bounce': {
          '0%, 60%, 100%': { transform: 'translateY(0)', opacity: '0.4' },
          '30%': { transform: 'translateY(-4px)', opacity: '1' },
        },
        'message-enter': {
          from: { opacity: '0', transform: 'translateY(8px)' },
          to: { opacity: '1', transform: 'translateY(0)' },
        },
      },
      animation: {
        'thinking-bounce': 'thinking-bounce 1.4s ease-in-out infinite',
        'message-enter': 'message-enter 200ms ease-out',
      },
    },
  },
  plugins: [],
};

export default config;
```

---

## 11. Icon Reference

All icons from `lucide-react`. Sizes noted per context.

| Usage | Icon Name | Size | Notes |
|-------|-----------|------|-------|
| Empty state | `MessageSquare` | `w-12 h-12` | Centered, `text-slate-300` |
| Send button | `ArrowUp` | `w-4 h-4` | Inside send button |
| Source citation (file) | `FileText` | `w-3 h-3` | Before source name |
| Source citation (URL) | `ExternalLink` | `w-3 h-3` | Before source name |
| List Resources chevron | `ChevronDown` | `w-3.5 h-3.5` | Rotates when expanded |
| Dialog warning | `AlertTriangle` | `w-10 h-10` | `text-red-500`, centered |
| Status dot | None | `w-2 h-2` | Pure CSS circle, no icon |

---

## 12. Component Inventory

Summary of all React components to implement:

| Component | Location | Description |
|-----------|----------|-------------|
| `AppLayout` | `layout.tsx` | Root layout with header, sidebar, chat area |
| `Header` | `components/Header.tsx` | App name, subtitle, status indicator |
| `Sidebar` | `components/Sidebar.tsx` | Container for Settings and KB panel |
| `SettingsSection` | `components/SettingsSection.tsx` | LLM info display |
| `KnowledgeBasePanel` | `components/KnowledgeBasePanel.tsx` | URL input, file upload, log, resources, clear |
| `ActivityLog` | `components/ActivityLog.tsx` | Color-coded scrollable log |
| `ResourceList` | `components/ResourceList.tsx` | Collapsible resource listing |
| `ChatArea` | `components/ChatArea.tsx` | Message area + input bar container |
| `MessageList` | `components/MessageList.tsx` | Scrollable message area with auto-scroll |
| `EmptyState` | `components/EmptyState.tsx` | Centered placeholder for no messages |
| `MessageBubble` | `components/MessageBubble.tsx` | User or bot message with styling |
| `SourceCitations` | `components/SourceCitations.tsx` | Source links below bot messages |
| `ThinkingIndicator` | `components/ThinkingIndicator.tsx` | Animated three-dot loader |
| `ChatInput` | `components/ChatInput.tsx` | Text input + send button bar |
| `ConfirmDialog` | `components/ConfirmDialog.tsx` | Reusable confirmation modal |
| `StatusIndicator` | `components/StatusIndicator.tsx` | Connection status dot + label |

---

*End of UI specification. This document provides all design decisions needed for implementation. The frontend developer should not need to make design choices -- every color, spacing value, class, animation, and state is defined above.*
