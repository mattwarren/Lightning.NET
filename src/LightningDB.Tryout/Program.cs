using System;
using System.Diagnostics;
using System.IO;

namespace LightningDB.Tryout
{
    class Program
    {
        static void Main(string[] args)
        {
            for (int i = 0; i < 3; i++)
                DoTest();
            for (int i = 0; i < 3; i++)
                DoTest(useRandomKeys: true);
        }

        private static void DoTest(bool useRandomKeys = false)
        {
            var env = new LightningEnvironment(CreateNewDirectoryForTest());
            env.MaxDatabases = 2;
            env.MapSize = 1000 * 1024 * 1025; // 100 MB
            env.Open();

            try
            {
                DoTestImpl(env, useRandomKeys);
            }
            finally
            {
                if (env != null)
                    env.Dispose();
            }
        }

        private static void DoTestImpl(LightningEnvironment env, bool useRandomKeys = false)
        {
            var numItemsToWrite = 1 * 1000; // One thousand
            //var numItemsToWrite = 10 * 1000; // Ten thousand
            //var numItemsToWrite = 100 * 1000; // One hundred thousand
            //var numItemsToWrite = 1 * 1000 * 1000; // 1 million
            //var numItemsToWrite = 10 * 1000 * 1000; // 10 million
            var randon = new Random(1773);
            var randomKey = new byte[sizeof(int)];
            Console.WriteLine("Using {0} keys", useRandomKeys ? "RANDOM" : "SEQUENTIAL");

            var writeTimer = Stopwatch.StartNew();
            // Need to specify DatabaseOpenFlags.IntegerKey if we want the items to be sorted, not entirely sure why though,
            // probably big/little endian related, see http://www.openldap.org/lists/openldap-bugs/201308/msg00050.html
            var dbConfig = new DatabaseConfiguration { Flags = DatabaseOpenFlags.Create | DatabaseOpenFlags.IntegerKey };
            using (var tx = env.BeginTransaction())
            using (var db = tx.OpenDatabase("test", dbConfig))
            {
                for (var i = 0; i < numItemsToWrite; ++i)
                {
                    if (useRandomKeys)
                    {
                        randon.NextBytes(randomKey);
                        tx.Put(db, randomKey, BitConverter.GetBytes(i));
                    }
                    else
                    {
                        tx.Put(db, BitConverter.GetBytes(i), BitConverter.GetBytes(i));
                    }
                }
                tx.Commit();
                var stats = tx.GetStats(db);
                Console.WriteLine("Stats\n  # Entries: {0:N0}\n  Depth: {1}\n  Page Size: {2}",
                                  stats.ms_entries.ToInt64(), stats.ms_depth, stats.ms_psize);
                Console.WriteLine("  Branch Pages: {0:N0}\n  Leaf Pages: {1}\n  Overflow Pages: {2}",
                                  stats.ms_branch_pages.ToInt64(), stats.ms_leaf_pages.ToInt64(), stats.ms_overflow_pages.ToInt64());
            }
            writeTimer.Stop();
            Console.WriteLine("Took {0,10:N2} ms ({1}) to WRITE {2:N0} values ({3,10:N0} WRITES/sec)",
                              writeTimer.Elapsed.TotalMilliseconds, writeTimer.Elapsed, numItemsToWrite,
                              numItemsToWrite / writeTimer.Elapsed.TotalMilliseconds * 1000.0);

            var readTimer = Stopwatch.StartNew();
            var readCounter = 0;
            using (var tx = env.BeginTransaction())
            using (var db = tx.OpenDatabase("test"))
            {
                tx.Get(db, BitConverter.GetBytes(1));
                using (var cursor = tx.CreateCursor(db))
                {
                    while (cursor.MoveNext())
                    {
                        var current = cursor.Current;
                        readCounter++;
                    }
                }
            }
            readTimer.Stop();
            Console.WriteLine("Took {0,10:N2} ms ({1}) to READ  {2:N0} values ({3,10:N0}  READS/sec)",
                              readTimer.Elapsed.TotalMilliseconds, readTimer.Elapsed, readCounter,
                              readCounter / readTimer.Elapsed.TotalMilliseconds * 1000.0);
            Console.WriteLine();
        }

        private static string CreateNewDirectoryForTest()
        {
            var testProjectDir = Path.GetDirectoryName(typeof(Program).Assembly.Location);
            var guid = Guid.NewGuid().ToString();
            //var path = Path.Combine(Directory.GetParent(testProjectDir).Parent.FullName, "TestDb", guid);
            var path = Path.Combine(testProjectDir, "TestDb", guid);
            //Console.WriteLine("Creating folder: {0}\n{1}", guid, path);
            Console.WriteLine("Creating folder: {0}", guid);
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
