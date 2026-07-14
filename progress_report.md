# Unity 2.5D 關卡與角色機制開發進度回報 (自 7/8 起)

本報告彙整了自 7/8 以來，在角色物理移動、2D 狼隻 AI 機動與動畫、機關觸發系統、受傷與死亡重生等機制的完整開發進度，包含修復的關鍵 Bug 與新增的腳本組件。

---

## 1. ⚙️ 版本控制與多人協作同步 (Git System)
*   **本地專案 Git 初始化**：將活躍專案目錄 `/Graduation_project-main/` 成功初始化為 Git 倉庫並追蹤。
*   **GitHub 遠端推送**：解決了磁碟空間爆滿導致的推送問題。目前所有本地場景設定、控制器檔案、去背圖片與代碼皆已 100% 成功 Push 至 GitHub 倉庫。
*   **多人同步**：與隊友 `andywang` 成功同步，包括最新的沙漠關卡 `desert.unity`、仙人掌物件貼圖與動畫資源。

---

## 2. 🐺 2D 狼隻 AI 追蹤、動畫與物理優化
*   **動畫代碼直驅 (Animator CrossFade)**：改寫了 `WolfSpriteAnimator.cs`。不再使用 Unity 複雜的 Animator 狀態機過渡線與參數，改由 C# 代碼在背景直接判定物理速度，呼叫 `CrossFade()` 在 `walking` (慢走)、`running` (快跑)、`backward` (後退) 之間進行平滑過渡。
*   **「123 木頭人」回頭退後機制**：修改了 `WolfEnemy.cs`。當主角轉身注視狼時（玩家面朝方向與狼追擊方向相反），狼會因被注視而以 `Retreat Speed` 向後倒退跑。
    *   *調整設定*：在 Inspector 中將 `Retreat Speed` 設為 `0`，可切換為「主角一回頭，狼便在原地罰站不動」的經典設定。
*   **鏡像轉向物理 Bug 修復**：
    *   *問題*：若直接對掛載 Rigidbody/Collider 的狼本體進行 `localScale.x = -1` 鏡像，會導致物理摩擦力失效、狼隨機漂移或飛天。
    *   *修復*：優化 `WolfSpriteAnimator.cs`，限制僅翻轉負責視覺的 **「子物件 (Child GameObject)」** 之縮放比例，物理本體維持不變，徹底解決物理引擎崩潰亂飄的 Bug，且完美避開了動畫 Keyframe 對 `flipX` 的衝突覆蓋。
*   **高跳防狼機制 (高度追蹤限制)**：
    *   *修復*：修改了 `WolfEnemy.cs`。若主角跳上高台，且主角 Y 軸高度高出狼超過 `3` 米（可於 `Stop Chase Height Difference` 調整）且處於懸空狀態，狼會自動停止追捕並原地煞車，直到主角踩回地面（觸地）時才會重新追蹤。

---

## 3. 🕹️ 機關與環境互動機制
*   **物體質量動態減速比率 (Dynamic Drag)**：
    *   修改了 `PlayerMovement.cs`。主角拉動物體時的移動速度，改為與物體剛體 (Rigidbody) 的 **質量 (mass)** 進行動態連動。
    *   *公式*：$\text{實際速度} = \text{速度} \times \frac{10}{10 + \text{物體質量}}$。推拉越重的物體，沉重阻力感越真實。
*   **順序顯現平台觸發器 (Sequential Reveal Trigger)**：
    *   優化 `SequentialRevealTrigger.cs`。移除了抽象 Collider 限制，解決了編輯器無法掛載到 2D 碰撞體物件上的問題。
    *   *Trigger By Player 開關*：勾選後允許主角直接踩踏觸發平台顯現，不需在腳本內手動拖曳主角物件。
    *   *物理碰撞支援*：新增 `OnCollision` 偵測。**平台不需要勾選 `Is Trigger`**。主角既能將其當作實體地板踩在上面行走，又同樣能順利觸發顯現平台。
*   **去背拉桿與解鎖巨石系統 (Lever & Rock Release)**：
    *   新增 `LeverSystem.cs`。遊戲開始時目標巨石會處於鎖定狀態（`isKinematic = true`）。當主角靠近拉桿按 **`E` 鍵** 後，拉桿播放拉下動畫，同時巨石解鎖並受重力自然掉落滾動。
    *   *去背拉桿貼圖*：利用 Python 程式去除 `lever.png` 灰色背景並自動裁切邊緣留白，生成無噪點的乾淨貼圖。
    *   *多元視覺反饋*：支持鏡像翻轉 (`FlipX`)、繞軸旋轉 (`Rotate`)、圖片替換 (`SpriteSwap`)，以及播放 Animator 動畫 (`PlayAnimation`)。

---

## 4. 🧬 動態出生點與死亡重生機制 (Spawner & Respawn)
*   **狼隻動態出生點系統 (Wolf Spawner)**：
    *   新增 `WolfSpawner.cs`。直接在場景中建立 Empty 物件作為出生點，即可動態克隆狼模板。
    *   *平滑淡入*：生成的新狼會在 `Fade Duration` 時間內平滑漸顯出來（不穿幫）。
    *   *距離啟動*：新生成的狼在主角拉開 `Start Chase Player Distance`（如 8 公尺）後，才會啟動 AI 追逐主角。
    *   *多種觸發模式*：碰撞器觸發（Box Collider）、主角 X 座標越線、或是直線距離靠近觸發。
*   **狼咬致死與死亡重生整合**：
    *   修改了 `PlayerMovement.cs` 與 `PlayerRespawnSystem.cs` 的協同運作。
    *   *跨層級搜尋*：升級為自動尋找父/子物件組件。當主角被狼咬住的數量達到上限（預設 3 隻）時，程式會自動跨層級呼叫重生系統。
    *   *重生與狀態重置*：觸發畫面逐漸變黑、黑屏轉場、顯示心靈雞湯鼓勵文字，並將主角傳送至最後安全存檔點。傳送時會自動將身上的狼咬計數歸零，恢復滿速，防範卡死。

---

## 📂 5. 檔案變動與新增列表

| 檔案路徑 | 變動類型 | 主要負責機制 |
| :--- | :--- | :--- |
| [WolfEnemy.cs](file:///Users/shouyichen/unityproject/Graduation_project-main/Assets/Codes/WolfEnemy.cs) | **修改** | 狼隻 AI 追蹤、回頭後退、高跳停止追逐、碰撞忽略 |
| [WolfSpriteAnimator.cs](file:///Users/shouyichen/unityproject/Graduation_project-main/Assets/Codes/WolfSpriteAnimator.cs) | **修改** | 狼隻 2D 動畫狀態代碼直驅、子物件縮放翻轉 Bug 修復 |
| [PlayerMovement.cs](file:///Users/shouyichen/unityproject/Graduation_project-main/Assets/Codes/PlayerMovement.cs) | **修改** | 質量減速、狼咬致死觸發、Warp 傳送時狀態重置 |
| [SequentialRevealTrigger.cs](file:///Users/shouyichen/unityproject/Graduation_project-main/Assets/Codes/SequentialRevealTrigger.cs) | **修改** | 移除抽象限制、主角踩踏觸發、實體碰撞 (IsTrigger=false) 觸發 |
| [RuinsDoor.cs](file:///Users/shouyichen/unityproject/Graduation_project-main/Assets/Codes/RuinsDoor.cs) | **修改** | 牆壁破壞，限定特定巨石名稱 (Clone) 防呆判定，防主角誤撞 |
| [LeverSystem.cs](file:///Users/shouyichen/unityproject/Graduation_project-main/Assets/Codes/LeverSystem.cs) | **[NEW]** | 控制拉桿互動、扳動動畫表現、目標巨石 kinematic 解鎖 |
| [WolfSpawner.cs](file:///Users/shouyichen/unityproject/Graduation_project-main/Assets/Codes/WolfSpawner.cs) | **[NEW]** | 狼隻動態生成、平滑漸顯淡入、拉開距離啟動 AI 追逐 |
| [ScreenFeedbackManager.cs](file:///Users/shouyichen/unityproject/Graduation_project-main/Assets/Codes/ScreenFeedbackManager.cs) | **[NEW]** | 動態漸層紅邊受傷貼圖生成、相機抖動、淡入半週期調整 |
| [lever.png](file:///Users/shouyichen/unityproject/Graduation_project-main/Assets/ruined/lever.png) | **[NEW]** | 經 Python 去背與白邊裁切裁邊處理的拉桿 Sprite 貼圖 |
