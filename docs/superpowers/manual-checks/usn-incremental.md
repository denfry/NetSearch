# Manual check — USN incremental refresh (requires Administrator)

1. As Administrator, index a local NTFS root (full MFT scan). Confirm the status/counts.
2. Create a new file under the root, edit an existing file's content, delete a third, rename a fourth.
3. Click Обновить. The refresh should be near-instant (no full rescan) and reflect:
   - new file present with correct size/date,
   - edited file's size/date updated,
   - deleted file gone,
   - renamed file shows the new name/path, old name absent.
4. Disable + re-enable the volume's journal (`fsutil usn deletejournal /d C:` then re-create), then Обновить
   → app detects the journal mismatch and performs a full rescan (no stale rows).
5. Run not-as-admin → crawler path, everything still works.
