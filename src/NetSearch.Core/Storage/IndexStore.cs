using Microsoft.Data.Sqlite;
using NetSearch.Core.Models;

namespace NetSearch.Core.Storage;

public sealed record IndexEntryState(
    long Id,
    string Name,
    string ParentPath,
    bool IsDir,
    long Size,
    long Modified,
    long? Frn);

public sealed class IndexStore : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly object _gate = new();

    public IndexStore(string dbPath)
    {
        _conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
        }.ToString());
        _conn.Open();
        // WAL + NORMAL: durable enough for a rebuildable index, no fsync per commit.
        // The rest trade a little RAM for markedly faster bulk writes and full-table loads:
        //   temp_store=MEMORY  — sorts/temp B-trees stay off disk
        //   mmap_size=256MB    — read pages straight from the mapping, fewer syscalls
        //   cache_size=-65536  — 64 MB page cache (negative = KiB)
        //   wal_autocheckpoint — checkpoint less often during a long bulk insert
        Exec("""
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA temp_store=MEMORY;
            PRAGMA mmap_size=268435456;
            PRAGMA cache_size=-65536;
            PRAGMA wal_autocheckpoint=2000;
            """);
    }

    public void Initialize()
    {
        lock (_gate)
        {
            Exec("""
                CREATE TABLE IF NOT EXISTS roots (
                  id INTEGER PRIMARY KEY AUTOINCREMENT,
                  path TEXT NOT NULL UNIQUE,
                  last_indexed INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS entries (
                  id INTEGER PRIMARY KEY AUTOINCREMENT,
                  root_id INTEGER NOT NULL,
                  name TEXT NOT NULL,
                  name_lower TEXT NOT NULL,
                  parent_path TEXT NOT NULL,
                  is_dir INTEGER NOT NULL,
                  size INTEGER NOT NULL,
                  ext TEXT NOT NULL,
                  modified INTEGER NOT NULL,
                  UNIQUE(root_id, parent_path, name)
                );

                -- Searching/filtering happens in memory over the snapshot loaded by LoadAll,
                -- never via SQL on these columns, so secondary indexes on name_lower/ext/
                -- modified/size were pure write-amplification (four extra B-tree updates per
                -- row) and disk bloat. Drop them; root_id lookups are already served by the
                -- leading column of the UNIQUE(root_id, parent_path, name) index.
                DROP INDEX IF EXISTS idx_entries_name_lower;
                DROP INDEX IF EXISTS idx_entries_ext;
                DROP INDEX IF EXISTS idx_entries_modified;
                DROP INDEX IF EXISTS idx_entries_size;
                """);

            AddColumnIfMissing("entries", "frn", "INTEGER");
            AddColumnIfMissing("roots", "usn_journal_id", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing("roots", "usn_next", "INTEGER NOT NULL DEFAULT 0");
        }
    }

    private void AddColumnIfMissing(string table, string column, string decl)
    {
        using var info = _conn.CreateCommand();
        info.CommandText = $"PRAGMA table_info({table});";
        using var r = info.ExecuteReader();
        while (r.Read()) if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
        r.Close();
        Exec($"ALTER TABLE {table} ADD COLUMN {column} {decl};");
    }

    public void SetUsnState(int rootId, long journalId, long nextUsn)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE roots SET usn_journal_id=$j, usn_next=$n WHERE id=$id;";
            cmd.Parameters.AddWithValue("$j", journalId);
            cmd.Parameters.AddWithValue("$n", nextUsn);
            cmd.Parameters.AddWithValue("$id", rootId);
            cmd.ExecuteNonQuery();
        }
    }

    public bool TryGetUsnState(int rootId, out long journalId, out long nextUsn)
    {
        lock (_gate)
        {
            journalId = 0; nextUsn = 0;
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT usn_journal_id, usn_next FROM roots WHERE id=$id;";
            cmd.Parameters.AddWithValue("$id", rootId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return false;
            journalId = r.GetInt64(0); nextUsn = r.GetInt64(1);
            return journalId != 0;
        }
    }

    public void RemoveByFrn(int rootId, IEnumerable<long> frns)
    {
        lock (_gate)
        {
            using var tx = _conn.BeginTransaction();
            using var cmd = _conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM entries WHERE root_id=$r AND frn=$f;";
            var pr = cmd.Parameters.Add("$r", SqliteType.Integer);
            var pf = cmd.Parameters.Add("$f", SqliteType.Integer);
            pr.Value = rootId;
            foreach (var f in frns) { pf.Value = f; cmd.ExecuteNonQuery(); }
            tx.Commit();
        }
    }

    public int UpsertRoot(string path)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT INTO roots(path) VALUES($p) ON CONFLICT(path) DO NOTHING;";
            cmd.Parameters.AddWithValue("$p", path);
            cmd.ExecuteNonQuery();

            using var sel = _conn.CreateCommand();
            sel.CommandText = "SELECT id FROM roots WHERE path=$p;";
            sel.Parameters.AddWithValue("$p", path);
            return Convert.ToInt32(sel.ExecuteScalar());
        }
    }

    public void SetRootIndexed(int rootId, long unixTime)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE roots SET last_indexed=$t WHERE id=$id;";
            cmd.Parameters.AddWithValue("$t", unixTime);
            cmd.Parameters.AddWithValue("$id", rootId);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<RootPath> GetRoots()
    {
        lock (_gate)
        {
            var list = new List<RootPath>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT id, path, last_indexed FROM roots ORDER BY id;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new RootPath(r.GetInt32(0), r.GetString(1), r.GetInt64(2)));
            return list;
        }
    }

    public void BulkUpsert(IEnumerable<FileEntry> entries)
    {
        lock (_gate)
        {
            using var tx = _conn.BeginTransaction();
            using var cmd = _conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO entries(root_id,name,name_lower,parent_path,is_dir,size,ext,modified,frn)
                VALUES($root,$name,$namel,$parent,$isdir,$size,$ext,$mod,$frn)
                ON CONFLICT(root_id,parent_path,name) DO UPDATE SET
                  is_dir=excluded.is_dir, size=excluded.size,
                  ext=excluded.ext, modified=excluded.modified, frn=excluded.frn;
                """;
            var pRoot = cmd.Parameters.Add("$root", SqliteType.Integer);
            var pName = cmd.Parameters.Add("$name", SqliteType.Text);
            var pNameL = cmd.Parameters.Add("$namel", SqliteType.Text);
            var pParent = cmd.Parameters.Add("$parent", SqliteType.Text);
            var pIsDir = cmd.Parameters.Add("$isdir", SqliteType.Integer);
            var pSize = cmd.Parameters.Add("$size", SqliteType.Integer);
            var pExt = cmd.Parameters.Add("$ext", SqliteType.Text);
            var pMod = cmd.Parameters.Add("$mod", SqliteType.Integer);
            var pFrn = cmd.Parameters.Add("$frn", SqliteType.Integer);

            foreach (var e in entries)
            {
                pRoot.Value = e.RootId;
                pName.Value = e.Name;
                pNameL.Value = e.NameLower;
                pParent.Value = e.ParentPath;
                pIsDir.Value = e.IsDir ? 1 : 0;
                pSize.Value = e.Size;
                pExt.Value = e.Ext;
                pMod.Value = e.Modified;
                pFrn.Value = (object?)e.Frn ?? DBNull.Value;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    /// <summary>
    /// Starts an atomic, streaming update of one root's index. All batches share one
    /// transaction, which amortizes commit overhead. Dispose without Complete rolls
    /// the transaction back and preserves the old index.
    /// </summary>
    public RootScanSession BeginRootScan(int rootId)
    {
        Monitor.Enter(_gate);
        try
        {
            return new RootScanSession(this, rootId);
        }
        catch
        {
            Monitor.Exit(_gate);
            throw;
        }
    }

    public void RemoveByIds(IEnumerable<long> ids)
    {
        lock (_gate)
        {
            using var tx = _conn.BeginTransaction();
            using var cmd = _conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM entries WHERE id=$id;";
            var p = cmd.Parameters.Add("$id", SqliteType.Integer);
            foreach (var id in ids)
            {
                p.Value = id;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    public void DeleteEntriesForRoot(int rootId)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM entries WHERE root_id=$r;";
            cmd.Parameters.AddWithValue("$r", rootId);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<FileEntry> LoadAll() => Load(null);
    public IReadOnlyList<FileEntry> LoadByRoot(int rootId) => Load(rootId);

    public IReadOnlyList<IndexEntryState> LoadRootState(int rootId)
    {
        lock (_gate)
        {
            var list = new List<IndexEntryState>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                "SELECT id,name,parent_path,is_dir,size,modified,frn FROM entries WHERE root_id=$r;";
            cmd.Parameters.AddWithValue("$r", rootId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new IndexEntryState(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt64(3) != 0,
                    reader.GetInt64(4),
                    reader.GetInt64(5),
                    reader.IsDBNull(6) ? null : reader.GetInt64(6)));
            }
            return list;
        }
    }

    private IReadOnlyList<FileEntry> Load(int? rootId)
    {
        lock (_gate)
        {
            var list = new List<FileEntry>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                "SELECT id,root_id,name,name_lower,parent_path,is_dir,size,ext,modified FROM entries"
                + (rootId is null ? ";" : " WHERE root_id=$r;");
            if (rootId is not null) cmd.Parameters.AddWithValue("$r", rootId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new FileEntry
                {
                    Id = r.GetInt64(0),
                    RootId = r.GetInt32(1),
                    Name = r.GetString(2),
                    NameLower = r.GetString(3),
                    ParentPath = r.GetString(4),
                    IsDir = r.GetInt64(5) != 0,
                    Size = r.GetInt64(6),
                    Ext = r.GetString(7),
                    Modified = r.GetInt64(8),
                });
            }
            return list;
        }
    }

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public sealed class RootScanSession : IDisposable
    {
        private readonly IndexStore _owner;
        private readonly int _rootId;
        private readonly SqliteTransaction _transaction;
        private readonly SqliteCommand _insert;
        private bool _completed;
        private bool _disposed;

        internal RootScanSession(IndexStore owner, int rootId)
        {
            _owner = owner;
            _rootId = rootId;
            _transaction = owner._conn.BeginTransaction();

            _insert = owner._conn.CreateCommand();
            _insert.Transaction = _transaction;
            _insert.CommandText = """
                INSERT INTO entries(root_id,name,name_lower,parent_path,is_dir,size,ext,modified,frn)
                VALUES($root,$name,$namel,$parent,$isdir,$size,$ext,$mod,$frn)
                ON CONFLICT(root_id,parent_path,name) DO UPDATE SET
                  name_lower=excluded.name_lower, is_dir=excluded.is_dir,
                  size=excluded.size, ext=excluded.ext,
                  modified=excluded.modified, frn=excluded.frn
                WHERE entries.name_lower<>excluded.name_lower
                   OR entries.is_dir<>excluded.is_dir
                   OR entries.size<>excluded.size
                   OR entries.ext<>excluded.ext
                   OR entries.modified<>excluded.modified
                   OR NOT (entries.frn IS excluded.frn);
                """;
            _insert.Parameters.AddWithValue("$root", rootId);
            _insert.Parameters.Add("$name", SqliteType.Text);
            _insert.Parameters.Add("$namel", SqliteType.Text);
            _insert.Parameters.Add("$parent", SqliteType.Text);
            _insert.Parameters.Add("$isdir", SqliteType.Integer);
            _insert.Parameters.Add("$size", SqliteType.Integer);
            _insert.Parameters.Add("$ext", SqliteType.Text);
            _insert.Parameters.Add("$mod", SqliteType.Integer);
            _insert.Parameters.Add("$frn", SqliteType.Integer);
            _insert.Prepare();
        }

        public void AddBatch(IEnumerable<FileEntry> entries)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (var entry in entries)
            {
                _insert.Parameters["$name"].Value = entry.Name;
                _insert.Parameters["$namel"].Value = entry.NameLower;
                _insert.Parameters["$parent"].Value = entry.ParentPath;
                _insert.Parameters["$isdir"].Value = entry.IsDir ? 1 : 0;
                _insert.Parameters["$size"].Value = entry.Size;
                _insert.Parameters["$ext"].Value = entry.Ext;
                _insert.Parameters["$mod"].Value = entry.Modified;
                _insert.Parameters["$frn"].Value = (object?)entry.Frn ?? DBNull.Value;
                _insert.ExecuteNonQuery();
            }
        }

        public int Complete(IEnumerable<long> removedIds, long indexedAt)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_completed) throw new InvalidOperationException("The root scan was already completed.");

            var removed = 0;
            using (var delete = _owner._conn.CreateCommand())
            {
                delete.Transaction = _transaction;
                delete.CommandText = "DELETE FROM entries WHERE id=$id;";
                var id = delete.Parameters.Add("$id", SqliteType.Integer);
                foreach (var removedId in removedIds)
                {
                    id.Value = removedId;
                    removed += delete.ExecuteNonQuery();
                }
            }

            using (var updateRoot = _owner._conn.CreateCommand())
            {
                updateRoot.Transaction = _transaction;
                updateRoot.CommandText =
                    "UPDATE roots SET last_indexed=$indexedAt WHERE id=$root;";
                updateRoot.Parameters.AddWithValue("$root", _rootId);
                updateRoot.Parameters.AddWithValue("$indexedAt", indexedAt);
                updateRoot.ExecuteNonQuery();
            }

            _transaction.Commit();
            _completed = true;
            return removed;
        }

        public void Dispose()
        {
            if (_disposed) return;
            try
            {
                _insert.Dispose();
                _transaction.Dispose(); // rolls back automatically when Complete was not called
            }
            finally
            {
                _disposed = true;
                Monitor.Exit(_owner._gate);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _conn.Close();
            _conn.Dispose();
            SqliteConnection.ClearAllPools();
        }
    }

    /// <summary>
    /// Probes the database file for integrity using a short-lived connection that is
    /// guaranteed to be closed before this method returns, so the file can be deleted
    /// if it is corrupt.
    /// </summary>
    private static bool IsDatabaseHealthy(string dbPath)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
        }.ToString();
        using var probe = new SqliteConnection(cs);
        try
        {
            probe.Open();
            using var cmd = probe.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (SqliteException)
        {
            return false;
        }
        finally
        {
            try { probe.Close(); } catch { /* best effort */ }
        }
    }

    public static IndexStore OpenWithRecovery(string dbPath, out bool recovered)
    {
        recovered = false;

        // First probe the database with a fully managed connection so that
        // we are guaranteed it is closed before attempting any file deletion.
        bool healthy = !File.Exists(dbPath) || IsDatabaseHealthy(dbPath);
        SqliteConnection.ClearAllPools();

        if (healthy)
        {
            try
            {
                var store = new IndexStore(dbPath);
                store.Initialize();
                return store;
            }
            catch (SqliteException)
            {
                // Fell through — treat as corrupt
                SqliteConnection.ClearAllPools();
            }
        }

        // delete corrupt db + sidecars, then recreate
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var p = dbPath + suffix;
            try { if (File.Exists(p)) File.Delete(p); } catch { /* best effort */ }
        }
        recovered = true;
        var fresh = new IndexStore(dbPath);
        fresh.Initialize();
        return fresh;
    }
}
