# 荒原「鳥群攻擊系統」現況報告（給 AI 閱讀）

> 產出時間：2026-10-05。以工作區實際程式為準，已 push 到 `main`（最後 commit `8e25964`）。
> 標「log 證據」的來自 `Editor.log`／`Logs/wonita_debug.log` 的實機紀錄；沒標的是讀程式的結論。
> **目前系統已經穩定，最近一輪實機統計健康（見第 6 節）。** 這份報告用來讓你看懂架構、確認有沒有遺漏，不是在求救。

---

## 1. 一句話架構

```
Detection（鳥偵測玩家）→ 建立 Attack Request 進全場佇列（鳥進 Pending，照常盤旋）
→ Scheduler 每幀檢查：風／鳥影／重生／傘下／放行間隔／同時攻擊上限（只延後、不丟需求）
→ 挑一張需求（優先分數）並指定類型（定點／預判，照固定字串循環）→ GrantAttack
→ Lock（鎖落點，之後不再重算）→ Warning（紅線）→ Hide（紅線關）→ Dive → Hit（重生）／被護盾彈開／插地
→ Remove（真的完成攻擊才消耗，SetActive(false)）
```

---

## 2. 檔案與職責

| 檔案 | 職責 |
|---|---|
| `Assets/Codes/IndividualBirdEnemy.cs` | 單隻鳥：狀態機（Idle／Pending／Warning／Diving／Stuck／Bounced）、偵測、鎖定、紅線、俯衝、命中、重置 |
| `Assets/Codes/BirdAttackScheduler.cs` | static 排程器＋`BirdAttackSchedulerRunner`（每幀 Tick）。需求佇列、放行、類型循環、統計、Debug log |
| `Assets/Codes/DesertBeatDirector.cs` | 荒原四拍導演（場景裡**手動掛了一份**）：開局統一覆寫每隻鳥的設定、寫入排程器參數、逐隻檢查（Audit） |
| `Assets/Codes/WindGustSystem.cs` | 風週期（吹 2.5 秒／停 3.5 秒，停的最後 1 秒是前兆），`IsTelegraphing`／`CurrentState` 被鳥讀取 |
| `Assets/Codes/GiantShadowPass.cs` | x≈137 鳥影，期間 `SuppressAllUntil`（有限壓制 3.5 秒） |
| `Assets/Codes/WindStopZone.cs` | x≥225 風永久停，`SuppressAllUntil = ∞`（永久壓制） |
| `Assets/Codes/PlayerRespawnSystem.cs` | 重生；有 `OnResettablesReset` 事件（排程器訂閱，重生時清佇列） |
| `Assets/鳥群系統_提案/…ScatteredFlock` | 裝飾鳥群：1 團、11 隻、`attackEnabled=0`，不攻擊 |

---

## 3. 鳥的分類（log 證據）

| 類型 | 數量 | 會攻擊 |
|---|---|---|
| `crow (n)`（`IndividualBirdEnemy`） | 執行時 **88 隻**（磁碟存檔曾只有 60，Editor 裡開著的場景比存檔多；導演會依拍移除一部分） | 會 |
| `ScatteredFlock` 裝飾鳥 | 1 團共 11 隻 | 不會 |

- 88 隻裡導演開局會移除：拍一（x<45）6 隻、拍二（45～135）每兩隻砍一隻 9 隻、拍四（x≥225）3 隻 → 約 **70 隻**會攻擊。
- 逐隻檢查（`[BIRD AUDIT]`）：88 隻設定全部一致（偵測 10、前搖 1.2、俯衝速 12…），唯一不同是 44 隻存的是舊版 `PlayerOffset`（現在預設忽略，統一走定點／預判）。88 隻都**沒有 Collider**（攻擊靠距離判定，不靠碰撞，屬設計）。縮放 21.47 是 FBX 本身縮放。

---

## 4. 偵測與放行規則（現況）

### 4.1 鳥端偵測（`IndividualBirdEnemy.Update`）
只有同時滿足才建立需求並進 Pending：
- `currentState == Idle`、`autoDetectPlayer`、`triggerMode != TriggerZoneOrCollisionOnly`
- `postRespawnDelayTimer <= 0`（重生後 4 秒內不偵測）
- **沒有永久壓制**（`SuppressAllUntil == ∞`，風停區 x≥225）。**有限壓制（鳥影 3.5 秒）不再擋偵測，只延後放行。**
- 沒有重生中、玩家重生後已動過（`IsPlayerMovingAfterRespawn`）、不在遮陽傘下
- `_attackDisabled == false`（存檔點之前還沒攻擊過的鳥，重生後預設不攻擊）
- 水平距離或 3D 距離 ≤ `detectionRange`（導演覆寫為 18）

### 4.2 排程器每幀（`BirdAttackScheduler.Tick`）
1. 清理：鳥被銷毀／未啟用／不再 Pending 的需求移除；結束的攻擊從 active 移除。
2. 全場阻擋（只延後，事件只在「開始被擋」那一刻記一次）：重生中／未移動（`OTHER`）、傘下（`OTHER`）、**風期間**（`WIND`，但見下）、鳥影壓制（`SHADOW`）、放行間隔未到或 active ≥ 有效上限（`BUSY`）。
3. 風的例外：**最後一座掩體之後的鳥（`originalPosition.x > WindFreeFromX`）不受風限制**。導演開局算出最後一座掩體 x（實測 **134.7**），風期間只放行這些鳥。
4. 挑一張需求：優先分數（越小越先）`= |dx| + (在玩家身後 ? behindPenaltyMeters : 0) − 等待秒數 × agingMetersPerSecond`；強制需求（`BirdAttackTriggerZone` 等直接觸發）插隊；同區（以玩家為中心分左／中／右，半寬 `zoneHalfWidth`）連續放行超過 `maxSameZoneInRow` 次就優先挑別區；同分用 `InstanceID` 決勝（可重現，沒有任何每幀亂數）。
5. 需求保留範圍：玩家離這隻鳥的水平距離 > `detectionRange × requestKeepRangeMultiplier` 才取消（`CANCEL Reason=PlayerFar`）。
6. 放行：攻擊類型照 `attackPattern` 字串循環（C＝定點、P＝預判，預設 `CPCPPCCPCPPC`）；`nextSlotTime = now + slotInterval`（每 `attacksPerRound` 次再加 `restSeconds`）；同時攻擊上限 `EffectiveCap = clamp(maxSimultaneous + 佇列長度/queuePressureStep, …, maxSimultaneousHard)`。

### 4.3 預判（`PredictPlayerX`，已改為迭代收斂）
- `predictedX` 固定點迭代 6 次：`T = clamp((前搖 + 鳥飛到預判點的時間) × predictionTimeMultiplier, minimumPredictionTime, maximumPredictionTime)`，`lead = clamp(vx × T, ±predictionDistanceLimit)`。
- 準度：以鳥名字＋第幾次預判做**穩定雜湊**（非每次 Random）：`predictionPreciseChance` 的攻擊打「剛好攔截點 + predictionPreciseOffset」，其餘打「攔截點 ± predictionLooseMin～Max」。
- `|vx| < 0.2` 直接打當下位置。
- **鎖定後落點固定**（`_lockedDiveTarget`），紅線只在 Warning 顯示，Dive 開始就關；所有取消／彈開／重置路徑都會關紅線。

### 4.4 命中
- 俯衝每幀 2D 距離 ≤ `hitRadius`（導演覆寫，目前存 1.8）。依序豁免：護盾（外圍 2.4 公尺彈開）、石化硬撐、演出鎖定／重生中、`harmless`、無敵。通過後 `retreatInsteadOfKill` 為假（導演 `birdHitRespawnsPlayer=true`）就 `TriggerRespawn()`。
- 俯衝路徑上 `SweepForSurface` 遇到任何非 Trigger 碰撞體就插地（log 證據：約 4% 的俯衝在離落點 > 2.5 公尺時被環境擋下，`[BIRD DIVE-END-ENV] Collider=…` 會印出擋住它的物件）。

### 4.5 重生
- 排程器訂閱 `PlayerRespawnSystem.OnResettablesReset`：佇列、放行計時、輪替歷史全清；每隻鳥各自 `ResetToInitialState()`：
  - **存檔點之前（`originalPosition.x <= 存檔點.x + 1`）且已攻擊過 → 永久消失（`ResetGone`），不刷新。**
  - 存檔點之前、**沒攻擊過** → 保持原樣盤旋，但 `_attackDisabled = true`，這一輪不偵測（導演 `birdsBehindCheckpointCanAttack` 可開回舊行為）。
  - 存檔點以後 → 完全刷新（`ResetRefresh`），重生後 4 秒才開始偵測。

---

## 5. 場景序列化值 vs 導演開局覆寫（**這是過去反覆出問題的地方**）

Unity 存場景會把 Inspector 的值寫進檔案，**程式裡改預設值對已存過的欄位無效**。目前的處理：
- 導演有 `tuningVersion` 檢查：`keepMyInspectorValues = false`（現況）且存檔版本 < 3 時，開局自動把排程數值換成程式建議值並印一行 log：放行間隔 0.2、每輪空檔 0.4、每輪 4 次、同時上限 6（壓力加成最多到 10）、需求保留範圍 3 倍、預判上限 5 秒／30 公尺、身後罰分 8、等待加分 2。
- 目前**場景裡實際存的導演值**（會被上面的檢查在執行時覆蓋，除非你勾 `keepMyInspectorValues`）：`slotIntervalSeconds 0.4`、`attacksPerRound 3`、`restAfterRoundSeconds 1.2`、`maxSimultaneousAttacks 3`、`maxSimultaneousHard 12`、`requestKeepRangeMultiplier 1.5`、`birdMaxPredictionTime 3`、`birdPredictionDistanceLimit 14`、`birdDetectionRange 18`。
- **使用者自己在導演上調過、且不在 v3 覆蓋清單內的值（所以仍有效）：**`predictionPreciseChance 0.9`、`predictionPreciseOffset -0.5`、`birdHitRadius 1.8`、`birdHitRespawnsPlayer 1`、`birdsIgnoreWindAfterLastShelter 1`、`windFreeMargin 0`、`enableBirdAttackDebug 1`。
- 每隻鳥身上也存了 `predictionPreciseChance 0.7` 等欄位，但導演開局會統一覆蓋成導演的值。

---

## 6. 最近一輪實機統計（`[BIRD STATS @結束]`，約 22 次重生的長 session，log 證據）

| 指標 | 數字 |
|---|---|
| Detection | 363 |
| Queue（進佇列） | 362（100%） |
| Slot（放行） | 242（**67%**；先前版本 26～39%） |
| Dive | 238（Attack→Dive 98%） |
| Hit（命中→重生） | 17（Dive→Hit 7%） |
| Cancel | **0**（需求保留範圍放寬後） |
| WindBlock／ShadowBlock／SchedulerBusy／OtherBlock | 5／1／227／17 |
| DiveEndedByEnvironment | 9（約 4%） |
| 佇列剩餘（結束時） | 3 |
| 平均放行等待 | 1.66 秒（最大佇列 21、平均同時攻擊 5.2 隻、等待 > 3 秒的 21 隻） |
| 預判／寬鬆 | LOCK 近 150 筆：準 65、寬鬆 11 |

---

## 7. 歷史上踩過的坑（避免重複）

1. 預判落點用 2 次迭代沒收斂，比實際會到的位置多約 2 公尺，一路跑的玩家剛好早鳥一步（已改 6 次迭代）。
2. 預判上限存在每隻鳥的場景值（1.2 秒／7 公尺），改程式預設無效，要由導演統一覆蓋。
3. 「排隊時先領號碼牌」的版本會讓佇列積壓、被放棄的牌佔位，266 次偵測只發動 24 次（已改為「放行當下才登記」，再改為中央佇列＋優先分數）。
4. 最早的優先權是「最早進佇列先放行」，風停時會讓已經被甩在身後的鳥先放行，前方新偵測到的鳥輪不到（已改為距離優先＋身後罰分＋等待加分）。
5. 舊版 `retreatInsteadOfKill`（10/1 加入，場景每隻鳥存 true）讓命中只逼退不重生，與 0904 定案「鳥維持殺死」不符（導演現在統一覆寫為命中重生）。
6. 鳥影壓制期間偵測到玩家的鳥曾被直接略過（現在只延後放行，需求保留）。
7. 重生後「存檔點之前已攻擊過的鳥」曾被我誤做成刷新回來當裝飾（導演欄位被舊存檔值蓋掉），已改成永久消失並換新欄位名。

---

## 8. 目前已知、尚未處理或設計如此的事

- **x≥225 風停區（`SuppressAllUntil=∞`）**：玩家走過後，佇列裡殘留的鳥不會再放行，是企劃定的「全片第一次完全靜止」，沒動。
- **88 隻 vs 磁碟 60 隻**：Editor 開著的場景比存檔多約 28 隻，若沒存檔，別人 clone 專案會少鳥；原因（誰加的）沒查。
- **Hit 率 7%、等待 > 3 秒 8%**：使用者目前覺得難度「還好」，沒有再調。
- 鳥沒有 Collider：玩家直接碰到鳥、`PlayerShield` 的重疊偵測（若依賴碰撞體）不會觸發；攻擊與護盾彈開走距離判定，不受影響。
- 預判／命中半徑／準度欄位在導演可調：`predictionPreciseChance`、`predictionPreciseOffset`、`predictionLooseMin/Max`、`birdHitRadius`。

---

## 9. 建議另一個 AI 驗證的事
1. 開 `enableBirdAttackDebug`，跑一次，核對 `[BIRD STATS]` 的轉換率與第 6 節是否一致。
2. 確認 `[DesertBeatDirector]` 開局那行有印「排程數值是舊版本…已換成最新建議值」以及「最後一座掩體 x=…」。
3. 在存檔點之後被打到重生，看 `[BIRD STATS @重生]` 的 `ResetGone`／`ResetDecor`／`ResetRefresh` 是否符合第 4.5 節。
4. 確認 Editor 場景與磁碟存檔的鳥數一致（88 vs 60）。
