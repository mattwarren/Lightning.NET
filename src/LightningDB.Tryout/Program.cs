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
            //var numItemsToWrite = 10 * 1000 * 1000;
            var numItemsToWrite = 1 * 1000 * 1000;
            var randon = new Random(1773);
            var randomKey = new byte[10];
            Console.WriteLine("Using {0} keys", useRandomKeys ? "RANDOM" : "SEQUENTIAL");

            var writeTimer = Stopwatch.StartNew();
            using (var tx = env.BeginTransaction())
            using (var db = tx.OpenDatabase("test", new DatabaseConfiguration { Flags = DatabaseOpenFlags.Create }))
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
            }
            writeTimer.Stop();
            Console.WriteLine("Took {0,10:N2} ms ({1}) to WRITE {2:N0} values ({3,8:N2} WRITES/msec)",
                              writeTimer.Elapsed.TotalMilliseconds, writeTimer.Elapsed, numItemsToWrite,
                              numItemsToWrite / writeTimer.Elapsed.TotalMilliseconds);

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
            Console.WriteLine("Took {0,10:N2} ms ({1}) to READ  {2:N0} values ({3,8:N2}  READS/msec)",
                              readTimer.Elapsed.TotalMilliseconds, readTimer.Elapsed, readCounter,
                              readCounter / readTimer.Elapsed.TotalMilliseconds);
            Console.WriteLine();
        }

        private static string CreateNewDirectoryForTest()
        {
            var testProjectDir = Path.GetDirectoryName(typeof(Program).Assembly.Location);
            var guid = Guid.NewGuid().ToString();
            //var path = Path.Combine(Directory.GetParent(testProjectDir).Parent.FullName, "TestDb", guid);
            var path = Path.Combine(testProjectDir, "TestDb", guid);
            //Console.WriteLine("Creating folder: {0}\n{1}", guid, path);
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
