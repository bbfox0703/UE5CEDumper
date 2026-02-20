# References & Future Extensions

> Moved from CLAUDE.md.

-----

## 參考來源

- `Encryqed/Dumper-7`：自動 offset 偵測邏輯（`OffsetFinder`、`ObjectArray.cpp`）
- `Spuckwaffel/UEDumper`：Live Editor UI 架構參考
- `UE4SS-RE/RE-UE4SS`：UE5 runtime reflection 另一角度實作
- Unreal Engine 原始碼（UObject.h, UStruct.h, FNamePool, FField）

-----

## 備用 / 未來擴展

- `UE5_WalkWorld()`：遍歷 UWorld 所有 Actor，加入 Object Tree 的 World 節點
- 加密 GObjects 支援（hook `InitObjectArrayDecryption`，參考 Dumper-7 介面）
- Object diff：快照比較，找出新增/刪除的 UObject
- Export to JSON/CSV：dump 完整架構到檔案
- CE Table 整合：UI 選取欄位後，自動產生對應的 CE address record
