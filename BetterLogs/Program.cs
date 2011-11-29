using System;
using System.Globalization;
using System.IO;

namespace BetterLogs
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 1)
            {
                // Console.WriteLine("BetterLogs filename.log <Entrée>");
                // return;
                args = new string[] { "BetterTest.log" };
            }

            var file_name = args[0];
            var log_file = new StreamReader(file_name);

            var better_logs = new StreamWriter(file_name.Replace(".log", "_better.log"));

            var provider = CultureInfo.InvariantCulture;
            var previous_time = DateTime.MinValue;

            const string Debug_NH = "DEBUG NHibernate.SQL";

            var line = "";
            var date_count = 0;
            var line_count = 0;
            double seconds = 0;
            var sql = "";
            var sql_trace = false;
            var previous_sql = "";
            var repeat_count = 0;
            while ((line = log_file.ReadLine()) != null)
            {
                if (line.StartsWith("2011-"))
                {
                    var time = DateTime.ParseExact(line.Substring(0, 23), "yyyy-MM-dd HH:mm:ss,fff", provider);
                    if (date_count > 0)
                    {
                        var duration = time.Subtract(previous_time);
                        seconds = Math.Round(seconds + duration.TotalSeconds, 3);
                        if (duration.TotalSeconds > 0.5)
                        {
                            // Affiche les requêtes lentes
                            Console.WriteLine(string.Format("{0}° : {1} + {2} => {3}", line_count, previous_time, duration.TotalSeconds, seconds));
                        }
                        if (sql != "")
                        {
                            repeat_count = RepeatCount(repeat_count, sql, previous_sql);
                            var repeat = repeat_count == 0 ? "" : (1 + repeat_count).ToString();
                            better_logs.WriteLine(string.Format("{0}\t{1}\t{2}\t{3}\t{4}", duration.TotalSeconds, seconds, repeat, IsDML(sql), SqlFormat(sql)));
                            previous_sql = sql;
                        }
                    }
                    previous_time = time;
                    date_count++;

                    var nh_pos = line.IndexOf(Debug_NH);
                    if (nh_pos != -1)
                    {
                        sql_trace = true;
                        sql = line.Substring(nh_pos + Debug_NH.Length).Trim();
                    }
                    else
                    {
                        sql_trace = false;
                        sql = "";
                    }
                }
                else if (sql_trace)
                {
                    sql += " " + line.Trim();
                }
                line_count++;
            }

            log_file.Close();
            better_logs.Close();

            Console.WriteLine();
            Console.WriteLine(string.Format("{0} requêtes SQL / {1} logs", date_count, line_count));
            Console.ReadLine();
        }

        /// <summary>
        /// Indique si la commande SQL est une instruction de mise à jour des données
        /// </summary>
        /// <param name="sql">Requête SQL à contrôler</param>
        /// <returns>Chaine vide si la requête débute par "SELECT" ou "x" sinon</returns>
        private static string IsDML(string sql)
        {
            // Suppression contenu inutile
            sql = sql.Replace("[lambda_method] - ", "").Trim().Substring(0, 10).ToUpper();

            // Teste s'il s'agit d'une simple requête SELECT
            if (sql.StartsWith("SELECT"))
            {
                return "";
            }
            return "x";
        }

        /// <summary>
        /// Découpe la requête SQL en deux parties : la commande et les paramètres
        /// </summary>
        /// <param name="sql">Requête SQL à analyser</param>
        /// <returns>Tableau de 2 chaines { commande ; paramètres }</returns>
        private static string[] SqlSplit(string sql)
        {
            // Suppression contenu inutile
            sql = sql.Replace("[lambda_method] -", "").Trim();

            // Recherche où commencent les paramètres
            // SELECT ... FROM Table WHERE Un = :p0, Deux = :p2 ; :p0 = 1 ...
            var param_pos = sql.IndexOf(":p0 = ");

            // Cas où le SQL ne contient pas de paramètres nommés
            // SELECT ... FROM Table WHERE Un = ?, Deux = 1 ; p0 = 1 ...
            var named = true;
            if (param_pos == -1)
            {
                param_pos = sql.IndexOf("p0 = ");
                named = false;
            }

            // Renvoie le SQL tel quel s'il n'y a pas de paramètres
            if (param_pos == -1)
            {
                return new string[] { sql, "" };
            }

            // Nomme les paramètres si besoin
            if (!named)
            {
                var arg_pos = sql.IndexOf(" ? ");
                var arg_index = 0;
                while (arg_pos != -1)
                {
                    var arg_name = "p" + arg_index.ToString();
                    sql = sql.Substring(0, arg_pos) + " :" + arg_name + " " + sql.Substring(arg_pos + 3);
                    sql = sql.Replace(", " + arg_name + " = ", ", :" + arg_name + " = ");
                    arg_index++;
                    arg_pos = sql.IndexOf(" ? ");
                }
                sql = sql.Replace("p0 = ", " :p0 = ");
                // => SELECT ... FROM Table WHERE Un = :p0, Deux = :p2 ; :p0 = 1 ...
            }

            // Sépare le SQL des paramètres
            param_pos = sql.IndexOf(":p0 = ");
            var args = sql.Substring(param_pos);
            sql = sql.Substring(0, param_pos);

            // Renvoie un tableau avec commande SQL et ses paramètres
            return new string[] { sql, args };
        }

        /// <summary>
        /// Détermine si 2 requêtes SQL sont identiques
        /// </summary>
        /// <param name="count">Nombre de répétition actuel</param>
        /// <param name="sql">Requête en cours</param>
        /// <param name="previous_sql">Requête précédente</param>
        /// <returns>Nombre de répétion mis à jour (ie à zéro ou incrémenté)</returns>
        private static int RepeatCount(int count, string sql, string previous_sql)
        {
            // Sépare les 2 requêtes en SQL + paramètre
            var parts = SqlSplit(sql);
            var previous_parts = SqlSplit(previous_sql);

            // Pas de répétition si les 2 SQL sont différents
            if (parts[0] != previous_parts[0])
            {
                return 0;
            }

            // Incrémente le nombre de répétition
            return count + 1;
        }

        /// <summary>
        /// Transforme une requête SQL loguée (avec paramètres) en requête utilisable sous TOAD
        /// </summary>
        /// <param name="sql">La commande SQL à ré-écrire</param>
        /// <returns>La commande SQL sans paramètres</returns>
        private static string SqlFormat(string sql)
        {
            // Sépare le SQL des paramètres
            var parts = SqlSplit(sql);
            sql = parts[0];
            var args = parts[1];

            // Renvoie le SQL si pas de paramètres
            if (args == "")
            {
                return sql;
            }

            // Remplace chaque paramètre du SQL par sa valeur
            var param_pos = args.IndexOf(":p");
            while (param_pos != -1)
            {
                args = args.Substring(param_pos);

                var type_pos = args.IndexOf("[Type: ");
                string[] arg = args.Substring(0, type_pos).Split('=');

                sql = sql.Replace(arg[0].Trim(), arg[1].Trim());

                args = args.Substring(type_pos);
                param_pos = args.IndexOf(":p");
            }

            // Renvoie le SQL déparamétré
            return sql;
        }
    }
}