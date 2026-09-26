# Wiki ja-JP translation style

The rulebook for the `<Page>-ja-JP.md` set of the GitHub Wiki (`UE5CEDumper.wiki.git`); the zh-TW
counterpart is [wiki-zh-TW-style.md](wiki-zh-TW-style.md), and the two share rules 1, 2, 5, 6 and 7.

**Why it exists.** The first ja-JP set (2026-06/07) is grammatical but was produced the same way
as the discarded zh-TW set: clause-by-clause from an English page that has since been found wrong
on almost every page. It carries English sentence shapes (「名前付きサブセットはより小さくなります」)
and the English page's errors (「ゲームがサーバー側で…スキャン」 — there is no server).
**The maintainer cannot read Japanese**, so nobody reviews this set by eye: quality comes from
these rules, an independent reviewer agent per batch, and textlint. Keep all three.

## Base standards

- **JTF 日本語標準スタイルガイド（翻訳用）** — <https://www.jtf.jp/pdf/jtf_style_guide.pdf>. The
  baseline for notation. A local copy may exist at `out/jtf/` (gitignored, machine-local).
- **Microsoft 日本語スタイルガイド / Microsoft terminology** — wins over JTF on katakana long
  vowels and on UI wording, as Microsoft zh-TW wording does for the zh-TW set.
- **Maintainer's decision (2026-09-26): put a half-width space between full-width and half-width
  text** (「UObject を表示します」, not 「UObjectを表示します」), per Microsoft. This overrides JTF
  3.1.1.

## Rules

1. **Translate from the English page**, which has been checked against the code; never patch the
   old ja-JP text.
2. **です・ます体 throughout** (JTF: do not mix 敬体 and 常体). Headings and table cells may be
   noun phrases.
3. **Rewrite the sentence, keep the meaning.** Prefer 「〜できます」 over 「〜することができます」;
   no pronouns the Japanese does not need (それ／これ referring back across sentences); avoid
   stacked passives 「〜によって〜されます」 and 「〜において」 where a plain particle works.
4. **UI element names stay in English, bold**, exactly as the UI shows them (**Scan Steam**,
   **Force Overwrite**) — the UI is English-only. Page links keep the `-ja-JP` suffix.
5. **Keep code, paths, identifiers, log text and quoted UI messages verbatim.**
6. **Punctuation**: full-width 、。（）「」：. No space inside or outside full-width brackets
   (JTF 3.3). Half-width digits and Latin letters.
7. **Numbers**: 400K → 40 万; ranges 10～30 秒.
8. **Katakana long vowel (Microsoft rule)**: words from English *-er / -or / -ar* take a final ー —
   サーバー、ユーザー、フォルダー、パラメーター、コンピューター、エディター、プレイヤー、
   マーカー、バッファー、ポインター、フィルター、スライダー、ヘッダー、ローダー. Words from *-y* do
   **not** — メモリ、プロパティ、ディレクトリ、カテゴリ、ライブラリ、エントリ、ユーティリティ、
   セキュリティ、クエリ.
9. **Katakana compounds** are written without a separator when established (オブジェクトツリー,
   チェックボックス); use a middle dot only where two long loanwords would be unreadable joined.
   Be consistent within a page.

## Glossary

| English | ✅ Use | ❌ Avoid / note |
|---|---|---|
| row / column | 行 / 列 | ⚠ the zh-TW set is the opposite (列 / 欄) — do not copy from it |
| offset | オフセット | |
| pointer | ポインター | ポインタ |
| instance / object / class | インスタンス / オブジェクト / クラス | |
| property / field | プロパティ / フィールド | |
| function | 関数 | ファンクション |
| struct / enum / array | 構造体 / 列挙型 / 配列 | |
| hook | フック | |
| inject / injection | 注入する / インジェクション | |
| dump | ダンプ | |
| scan | スキャン | |
| snapshot | スナップショット | |
| tooltip | ツールヒント | |
| drop-down | ドロップダウン | |
| checkbox | チェックボックス | |
| tab (UI) | タブ | |
| log file | ログファイル、ログ | |
| settings / options | 設定 / オプション | |
| default | 既定値、既定 | デフォルト is acceptable in running text; be consistent per page |
| script (incl. Lua / CE) | スクリプト | |
| cheat / cheat table | チート / チートテーブル | |
| clipboard | クリップボード | |
| process (OS) | プロセス | |
| thread | スレッド | |
| runtime | 実行時 | ランタイム only in a proper name |
| build (verb / noun) | ビルドする / ビルド | |
| hard-coded | ハードコード | |
| server-side (inside the game DLL) | ゲーム内の DLL 側で | サーバー側 — there is no server |
| named pipe | 名前付きパイプ | |
| cache | キャッシュ | |
| partial match / substring | 部分一致 | |
| space = AND | スペース区切りの AND 検索（すべてのキーワードに一致） | |
| crash | クラッシュ | |
| anti-tamper / anti-cheat | 改ざん防止 / アンチチート | |
| launcher | ランチャー | |
| Steam library | Steam ライブラリ | Steam's own ja UI |
| container | コンテナー | Microsoft -er rule |
| slider / radio button / badge | スライダー / ラジオボタン / バッジ | |
| leaf (node) / hop | リーフ / ホップ | |
| breadth-first search | 幅優先探索 | |
| disassembler / memory viewer | 逆アセンブラー / メモリビューアー | |
| hex view | 16 進表示 | |
| loader lock / shim | ローダーロック / シム | |
| cooked (UE) | クック済み | |
| CDO | クラスのデフォルトオブジェクト（CDO） | spell out on first use per page |
| Recycle Bin | ごみ箱 | Windows ja UI name |
| "our" proxy / DLL | 本ツールのプロキシ / DLL | not 私たちの |
| idempotent | 「すでに ON なら何もしません」 | describe it; no single word |
| xref / cross-reference | 相互参照 | |
| reverse lookup | 逆引き | |
| client-side (filter) | UI 側 | |
| breadcrumb | パンくずリスト | |
| spawn / spawned | 生成する / 生成済み | |
| heuristic | 推定 | |
| live instance / object | 生存中のインスタンス / オブジェクト | |
| seed / starting object | 起点のオブジェクト | |
| back-reference | たどってきた側を指し返す参照 | ❌ 逆参照 (reads as *dereference*) |
| amber / teal | 琥珀色 / 青緑色 | |
| hold (Force field) / freeze | 保持 / 固定 | keep the two mechanisms distinct |
| armed (Force field) | 待機状態（armed） | |
| baseline | ベースライン | |
| predicate (Value Search) | 条件 | |
| projected value (Class Pivot) | 射影値 | |
| curated | 選別した | ❌ 厳選した / 絞り込んだ (adds intent / implies the user filtered) |
| trampoline | トランポリン | |
| entry (CT) | エントリ | Microsoft -y rule |

## Checking a page

`out/textlint-ja/` (gitignored, machine-local) holds textlint with `textlint-rule-preset-jtf-style`
(3.1.1 switched off per the decision above) and `textlint-rule-ja-space-between-half-and-full-width`
(`space: "always"`):

```bash
cd out/textlint-ja && npx textlint <path-to>/<Page>-ja-JP.md
```

JTF 3.3 (bracket spacing) has fired on list lines that contain inline code — read each hit
before "fixing" it, and do not run `--fix` without reading the diff it produces.
