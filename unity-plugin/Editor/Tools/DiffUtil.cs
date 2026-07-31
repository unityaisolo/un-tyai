using System;
using System.Collections.Generic;

namespace UnityAI.Tools
{
    public struct DiffLine
    {
        public char Tag;   // ' ' context, '+' eklenen, '-' silinen
        public string Text;
    }

    /// <summary>Basit LCS tabanlı satır diff'i (Cursor tarzı +/- gösterim için).</summary>
    public static class DiffUtil
    {
        public static List<DiffLine> LineDiff(string oldText, string newText)
        {
            var a = (oldText ?? "").Replace("\r\n", "\n").Split('\n');
            var b = (newText ?? "").Replace("\r\n", "\n").Split('\n');
            int n = a.Length, m = b.Length;
            var dp = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    dp[i, j] = a[i] == b[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);

            var res = new List<DiffLine>();
            int x = 0, y = 0;
            while (x < n && y < m)
            {
                if (a[x] == b[y]) { res.Add(new DiffLine { Tag = ' ', Text = a[x] }); x++; y++; }
                else if (dp[x + 1, y] >= dp[x, y + 1]) { res.Add(new DiffLine { Tag = '-', Text = a[x] }); x++; }
                else { res.Add(new DiffLine { Tag = '+', Text = b[y] }); y++; }
            }
            while (x < n) { res.Add(new DiffLine { Tag = '-', Text = a[x] }); x++; }
            while (y < m) { res.Add(new DiffLine { Tag = '+', Text = b[y] }); y++; }
            return res;
        }
    }
}
