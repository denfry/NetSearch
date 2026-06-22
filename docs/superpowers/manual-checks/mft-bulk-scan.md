# Manual check — MFT bulk scan (requires Administrator)

1. Add a local root (e.g. `C:\Users\<you>\Documents`) in Settings.
2. Run NetSearch **as Administrator**. Click Обновить.
   - Expect the scan to finish far faster than an unprivileged run; status shows the indexed count.
3. Spot-check 5 files in the grid: Name, Path, Size, and Modified date match Explorer.
4. Compare totals: run once as admin (MFT) and once normally (crawler) on the same root; counts should be within a small delta (open handles / transient files).
5. Filters: size and date filters return sensible results for the MFT-indexed root.
6. Fallback: run **not** as admin → app still works (crawler), no errors.
7. Non-NTFS / UNC root → always crawler, no errors.
