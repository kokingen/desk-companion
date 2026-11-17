# OPENCODE 031 — Performance Review

Date: 2026-11-17
Mode: read-only static review. No build, test run, deployment, device interaction, settings change, or private-data access was performed. The only file written is this review.

## Verdict

No blocking correctness or data-accuracy regression was found in the three changes.

- `Collector.NetworkLoop` start-based `lastRead` correctly restores a ~100 ms cadence for 20–100 ms sample work, and network-rate values remain based on real per-adapter read times.
- `CompanionPerformance.displayed` still updates the DOM counter and latency histograms when not recording, while skipping the UI-thread WebView visual-fence work. That is the intended normal-usage behaviour.
- `.face,.monitor{contain:layout paint}` and `#avatar{transform:translateZ(0)}` are present in both the UI source and the shipped Android asset. Static inspection did not find a layout/containing-block regression, but paint-containment clipping and the extra composited layer were not visually verified on a device.

One behavioural caveat is documented: if a `NetworkLoop` iteration takes longer than the 100 ms timer period, the new gate allows an immediate catch-up sample, whereas the old end-based timestamp self-throttled the loop. This can produce back-to-back sampling under pathological overrun; it is a load tradeoff, not an incorrect-data bug. Details below.

## Scope and method

Inspected:

- `agent/Max3.Agent/Collector.cs` (`NetworkLoop`, `NetworkRate`, snapshot construction)
- `android/app/src/main/java/net/oxog/phonebox/deskpanel/CompanionPerformance.kt`
- `android/app/src/main/java/net/oxog/phonebox/deskpanel/CompanionActivity.kt` (bridge/delivery callers)
- `tools/companion-ui/src/main.js`, `tools/companion-ui/src/style.css`
- `android/app/src/main/assets/companion/app.js`, `style.css`
- `tools/companion-ui/build.mjs`, `browser-test.mjs`, `test.mjs`
- `tools/Measure-CompanionStability.ps1`
- `agent/Max3.Tests/Program.cs`

Also compared the tracked `Collector.cs` change with `git diff`. `CompanionPerformance.kt` is currently untracked/new in this working tree, so its current contents were reviewed rather than a commit diff.

## 1. Collector.NetworkLoop start-based gating

### Change

`agent/Max3.Agent/Collector.cs:99-101`:

```csharp
// Gate coalesced ticks from the start of this real sample. Discovery or
// native reads taking >20 ms must not suppress the next 100 ms tick.
lastRead=now;
```

Previously this was `lastRead=Mono()` after the sample body. The gate itself is unchanged at `Collector.cs:76`:

```csharp
if(now-lastRead<80)continue;
```

### Why the change is correct for the intended range

Let a sample start at `S` and take `W` ms. The next nominal tick is at `S+100`.

- Old (`lastRead` = sample end): the next tick is skipped when `100 - W < 80`, i.e. `W > 20`. For `20 < W < 100`, the loop therefore fell to a ~200 ms cadence (skip the 100 ms tick, sample at the 200 ms tick).
- New (`lastRead` = sample start): the next tick sees `100 - 0 = 100 >= 80` and samples. A ~100 ms cadence is preserved for all `0 <= W < 100` ms.
- For `W <= 20` ms, both versions already passed the gate, so the change does not alter the fast path.
- The `80` ms constant still exists, but with start-based timing and a fixed 100 ms `PeriodicTimer`, normal ticks are ~100 ms after the start; the guard can no longer suppress the common post-discovery tick. It only rejects a tick that arrives less than 80 ms after a start, which the fixed-period timer does not normally produce.

So the stated goal — “>20 ms discovery/native work must not suppress the next 100 ms tick” — is achieved exactly at the old `W > 20` boundary.

### No rate/interval regression

`lastRead` is used only by the gate. The actual rate calculation still uses per-adapter read timestamps:

- `sampled=Mono()` is taken after each `GetIfEntry2` call and passed to `NetworkRate.Sample` (`Collector.cs:87-92`).
- `NetworkRate.Sample` computes `dt=now-at` from those real read times and returns that `IntervalMs` (`Collector.cs:186-194`).
- The snapshot’s `MonotonicMs` remains the end-of-sample `Mono()` (`Collector.cs:98`).

Therefore using the sample start for gating does not shorten or fabricate network intervals, and `latest` timestamps stay end-of-work consistent.

### Caveat: sustained overrun has no idle backoff

If `W >= 100` ms, `.NET` `PeriodicTimer` delivers the missed tick immediately after the body completes. With `lastRead=S`, the catch-up tick sees `W >= 80` and passes the gate, so another sample starts immediately. The loop then runs back-to-back (start-to-start cadence ≈ `W`) instead of the old behaviour, which skipped the immediate catch-up (0 ms since the end) and sampled again on the next 100 ms boundary.

- Old with e.g. `W=105` ms: roughly one sample per ~200 ms (about 52% duty).
- New with `W=105` ms: roughly one sample per ~105 ms (about 100% duty).

This is only reachable if discovery and native reads become pathologically slow; normal `GetIfTable2Ex` + `GetIfEntry2` work is far below 100 ms. It is a potential CPU/thread-pool load increase in an already degraded case, not a wrong-value regression. No clamp or backoff is currently present.

### Coverage

`agent/Max3.Tests/Program.cs:8-25` tests `NetworkRate` math and `Collector.Kind`, but there is no test of the `NetworkLoop` tick/gate behaviour. The change relies on timer semantics and must be reasoned about statically unless a deterministic gate/timing test is added.

## 2. CompanionPerformance.displayed early return

### Current behaviour

`CompanionPerformance.kt:61-79`:

1. Deduplicate by `session:sequence` (`:62-64`).
2. Increment `dom` (`:64`).
3. Update the age/uncertainty histograms under `histogramLock` (`:65-66`).
4. Emit the recorded `dom` row and trace only when `recording` (`:67-68`).
5. `if(!recording)return` (`:69-70`).
6. Only while recording, post the WebView visual-state fence on the UI thread (`:71-78`).

This matches the claim: normal (non-recording) usage skips `runOnUiThread`, `findViewById`, and `postVisualStateCallback`, while DOM counters and histograms continue to update.

### Consumers are unaffected

- `status()` still exposes `visualReady` (`CompanionPerformance.kt:82`), but `visual` is only incremented by the fence callback (`:75`). Outside recording it therefore stays at its prior value (normally 0). That is semantically expected but worth knowing.
- `tools/Measure-CompanionStability.ps1:78-81` consumes `receivedStates`, `domUpdates`, and `coalescedStates`; it does not depend on `visualReady`, so stability measurements that do not record a capture are unaffected.
- `Bridge.displayed` still parses the JSON for every call (`CompanionActivity.kt:175`). The early return removes UI-thread profiling work, not the bridge-side parse; the change should not be described as eliminating all per-display overhead.
- When recording, the first state after starting is still counted for DOM/histograms but will not get a visual fence if it was already deduplicated before recording began. That is an acceptable measurement-start boundary effect, not a regression.

### Minor observations (not blockers)

- `lastDomKey` is guarded with `synchronized(this)`, the same monitor used by `@Synchronized start()/stop()`. `stop()` joins the frame-metrics thread for up to 1 s while holding that monitor, so a bridge-thread `displayed` call can block briefly during stop. This is pre-existing in the new file and only affects teardown of a recording.
- The visual fence uses `sequence` as the WebView request id. AOSP’s `WebView.postVisualStateCallback` does not itself reject id 0, and the result here is only a counter increment, so a missing/zero sequence is not a crash risk in this code path. Uniqueness across recording sessions is not asserted, but is not required by this consumer.

## 3. CSS containment and avatar compositing

### Presence in source and shipped asset

Both files contain the same line 3:

- `tools/companion-ui/src/style.css:3`
- `android/app/src/main/assets/companion/style.css:3`

```css
.face,.monitor{contain:layout paint}#avatar{transform:translateZ(0)}
```

`tools/companion-ui/build.mjs:16` copies `src/style.css` and `src/index.html` into the Android asset directory, so the committed asset and the UI source are currently consistent. The compiled `assets/companion/app.js` also contains the new `displayed` call and the 100 ms status-only timer (`setInterval(()=>Ze(!0),100)`), so the asset is not stale relative to `src/main.js`.

### Layout/containing-block analysis

- `.face` is already `position:relative;overflow:hidden` (`style.css:1`). Adding `layout` containment does not change its role as the containing block for its absolutely positioned children; `paint` containment reinforces the existing clipping.
- `.monitor` has no positioned descendants. Its children (`.numbers`, `.live-trend`, `.network`, `footer`) are in normal flow/flex layout, and `dialog#history` is a sibling outside `main` (`src/index.html:12-13`), so it is unaffected by `contain` on `.monitor`.
- `contain:layout` does not apply size containment, so the grid/flex sizing of the 65%/35% layout and the `flex:1` chart area remain valid.
- `contain:paint` clips descendants to the padding box. The monitor’s children start inside its `20px 24px 14px` padding; the face’s header/avatar/labels are inside the existing clipped face. No descendant is intentionally painted outside those boxes except `#dot`’s small shadow, which is well inside `.face`. Static inspection found no clipping regression; rounded-corner clipping at the monitor’s 26px top corners and any focus/shadow edges were not visually tested.
- Containment creates stacking contexts, so `header{z-index:2}` is now scoped inside `.face`. It only needs to sit above the avatar/labels in the same face, so this is not a functional change. `dialog` remains in the top layer.
- `contain` is supported well before the build target `chrome89` (`build.mjs:15`) and Android 13 WebView, so there is no compatibility fallback concern.

### Avatar layer

`#avatar{transform:translateZ(0)}` promotes the avatar container to its own composited layer without changing layout. The avatar library’s dist code writes styles/size on the `.bs-avatar` child (`tools/companion-ui/node_modules/@bible-strong/avatar-web/dist/index.js:16,68`) and animates SVG attributes; no root `#avatar` inline transform was found, so the hint is not overridden today. If the library later writes an inline `transform` on the container, the inline style would win and the hint would simply be ineffective, not broken.

Cost: one additional composited layer. That is typically small and intentional here; it was not measured for memory/heat on the reference device in this review.

## 4. Adjacent UI timing change

`tools/companion-ui/src/main.js:16-22,119` changed the 200 ms full `update()` timer to a 100 ms status-only tick:

- Real state delivery still runs the full update.
- The status-only path only refreshes freshness text and calls `drawTrend()` (itself throttled to 500 ms at `main.js:98`).
- `displayed` is posted for each accepted state (`main.js:58`), after the monotonic duplicate check at `main.js:44`.

This is consistent with the performance intent. Minor behavioural note: while the history dialog is open and no new state arrives, detail lines (timing/disk/Wi-Fi list) are no longer refreshed by the 100 ms tick because the fast path returns before `detailLive()`. With normal state delivery this is still refreshed on each state; it only matters on a stale/offline dialog. Not a correctness defect for the stated goal.

## 5. Test scope — what was and was not verified

Not run in this review:

- `npm test` / `node --test`
- `browser-test.mjs` (Playwright)
- .NET native checks (`agent/Max3.Tests`)
- Android lint/build/unit tests
- Any real device, Agent service, Bluetooth link, or settings change

Existing coverage relevant to this review:

- `tools/companion-ui/test.mjs`: model/trend unit tests (`fresh`, `nicText`, `speed`, `ram`, `aiState`, trend deduplication). No test for `displayed`, the status-only timer, or CSS.
- `agent/Max3.Tests/Program.cs:8-25`: `NetworkRate` math and `Collector.Kind`. No test for the `NetworkLoop` gate.
- `tools/companion-ui/browser-test.mjs:21-25` exercises the shipped Android assets in Chromium at 360×720, 320×568, and 400×800, asserting no horizontal overflow, chart placement between metrics and network, footer inside the viewport, and minimum chart height. If run, it would catch gross layout regressions from the CSS containment change; it does not test rounded-corner clipping, shadows, Android WebView compositing, or the Kotlin visual fence.
- Android source search found no unit/instrumentation test referencing `CompanionPerformance` or `displayed`.
- `docs/VALIDATION.md` reports broader historical UI/native/device runs, but those were not re-executed here and do not specifically assert these three changes.

Conclusion on confidence: static logic for the collector gate and the `displayed` control flow is high confidence; runtime cadence under overrun, real Android WebView paint-containment visuals, and the composited-layer cost remain unverified by this review. No “passed on device” claim should be made from this document.

## Recommended follow-ups (not performed)

1. Optional collector hardening: preserve the intended 100 ms cadence for 20–100 ms work, but add an explicit overrun backoff when `W >= 100` ms (for example, skip the immediate `PeriodicTimer` catch-up after an over-budget sample, or gate on completion time only for overrun). This avoids the back-to-back sampling load described above.
2. Add a deterministic test for the gate decision (extracting “should sample now” into a pure function with injected start/completion timestamps would avoid needing real timers).
3. Run `browser-test.mjs` and a short device recording pass; visually check the monitor’s rounded top corners and face edges under `contain:paint`, and confirm `visualReady` still advances only during recording.
4. Keep using the generated-asset check (`build.mjs`) whenever CSS/JS changes, since the shipped Android files are copied artifacts and can silently diverge from `src/`.
