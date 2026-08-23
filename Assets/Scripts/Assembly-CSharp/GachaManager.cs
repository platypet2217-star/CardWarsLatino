using System;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GachaManager : ILoadable
{
	private class GachaWeightPair
	{
		public string name;

		public int weight;

		public GachaWeightPair(string n, int w)
		{
			name = n;
			weight = w;
		}
	}

	private class GachaWeightTable
	{
		public int totalWeight;

		public List<GachaWeightPair> weights = new List<GachaWeightPair>();

		public void Add(string name, int weight)
		{
			totalWeight += weight;
			weights.Add(new GachaWeightPair(name, totalWeight));
		}

		public string Pick()
		{
			int rnum = UnityEngine.Random.Range(1, totalWeight);
			GachaWeightPair gachaWeightPair = weights.Find((GachaWeightPair p) => rnum <= p.weight);
			if (gachaWeightPair == null)
			{
				return null;
			}
			return gachaWeightPair.name;
		}
	}

	private class GachaScheme
	{
		public DateTime start;

		public DateTime end;

		public bool isPremium;

		public int coins;

		public int gems;

		public string party;

		public List<string> columns;
	}

	public const string CardStandardGachaColumn = "DailyGiftStandard";

	public const string CardGoldGachaColumn = "DailyGiftGold";

	public const string CardObsidianGachaColumn = "DailyGiftObsidian";

	public const string CardHalloweenGachaColumn = "DailyGiftHalloween";

	private Dictionary<string, Dictionary<string, int>> weightDict;

	private List<GachaScheme> schemes;

	private Dictionary<string, PartyInfo> parties;

	private GachaWeightTable currNormalWeights;

	private GachaWeightTable currPremiumWeights;

	private GachaWeightTable DailyGiftNormal;

	private GachaWeightTable DailyGiftGold;

	private GachaWeightTable DailyGiftObsidian;

	private static GachaManager instance;

	public static GachaManager Instance
	{
		get
		{
			if (instance == null)
			{
				instance = new GachaManager();
			}
			return instance;
		}
	}

    public IEnumerator Load()
    {
        Dictionary<string, object>[] data = SQUtils.ReadJSONData("db_GachaWeights.json");
        if (LoadingManager.ShouldYield())
        {
            yield return null;
        }
        weightDict = new Dictionary<string, Dictionary<string, int>>();
        Dictionary<string, object>[] array = data;
        foreach (Dictionary<string, object> dict in array)
        {
            string cardID = TFUtils.LoadNullableString(dict, "CardID");
            if (string.IsNullOrEmpty(cardID) || weightDict.ContainsKey(cardID))
            {
                continue;
            }
            Dictionary<string, int> cardDict = new Dictionary<string, int>();
            weightDict.Add(cardID, cardDict);
            foreach (string key in dict.Keys)
            {
                if (!(key != "CardID") || !(key != "Quality"))
                {
                    continue;
                }
                if (key == "Quality")
                {
                    switch (TFUtils.LoadString(dict, key))
                    {
                        case "Gold":
                            cardDict.Add(key, 1);
                            break;
                        default:
                            cardDict.Add(key, 0);
                            break;
                    }
                }
                else
                {
                    int value = TFUtils.LoadInt(dict, key, 0);
                    cardDict.Add(key, value);
                }
            }
            if (LoadingManager.ShouldYield())
            {
                yield return null;
            }
        }
        data = SQUtils.ReadJSONData("db_GachaSchedule.json");
        if (LoadingManager.ShouldYield())
        {
            yield return null;
        }
        schemes = new List<GachaScheme>();
        Dictionary<string, object>[] array2 = data;
        foreach (Dictionary<string, object> dict2 in array2)
        {
            string stime = TFUtils.LoadNullableString(dict2, "StartDate");
            string etime = TFUtils.LoadNullableString(dict2, "EndDate");
            GachaScheme newScheme = new GachaScheme();
            if (SQUtils.StringEqual(stime.ToLower(), "default"))
            {
                newScheme.start = DateTime.MinValue;
                newScheme.end = DateTime.MaxValue;
            }
            else
            {
                // =============================================================
                // INTERCEPCIÓN DINÁMICA DE FECHAS PARA EL GACHA
                // =============================================================
                string sTimeCorregido = AjustarAnioGachaString(stime, 0);
                string eTimeCorregido = AjustarAnioGachaString(etime, 0);

                try
                {
                    // Si al forzar el año actual, el fin resulta menor que el inicio (ej. cambia de año en Año Nuevo)
                    // recalculamos la fecha de fin sumándole un año de margen.
                    if (DateTime.Parse(eTimeCorregido) < DateTime.Parse(sTimeCorregido))
                    {
                        eTimeCorregido = AjustarAnioGachaString(etime, 1);
                    }
                }
                catch { }

                // Asignamos las cadenas corregidas a las variables de lectura originales
                stime = sTimeCorregido;
                etime = eTimeCorregido;
                // =============================================================

                try
                {
                    newScheme.start = DateTime.Parse(stime);
                }
                catch (FormatException)
                {
                    throw;
                }
                try
                {
                    newScheme.end = DateTime.Parse(etime);
                }
                catch (FormatException)
                {
                    throw;
                }
            }
            newScheme.isPremium = SQUtils.StringEqual("premium", TFUtils.LoadString(dict2, "ChestType", string.Empty).ToLower());
            newScheme.coins = TFUtils.LoadInt(dict2, "Coins", 0);
            newScheme.gems = TFUtils.LoadInt(dict2, "Gems", 0);
            newScheme.party = TFUtils.LoadString(dict2, "Party", string.Empty);
            newScheme.columns = new List<string>();
            for (int i = 1; i <= 10; i++)
            {
                string newCol = TFUtils.TryLoadString(dict2, "Weight" + i);
                if (string.IsNullOrEmpty(newCol))
                {
                    break;
                }
                newScheme.columns.Add(newCol);
            }
            schemes.Add(newScheme);
            if (LoadingManager.ShouldYield())
            {
                yield return null;
            }
        }
        schemes.Sort(delegate (GachaScheme a, GachaScheme b)
        {
            double totalSeconds = (a.end - a.start - (b.end - b.start)).TotalSeconds;
            if (totalSeconds > 0.0)
            {
                return 1;
            }
            return (totalSeconds < 0.0) ? (-1) : 0;
        });
        data = SQUtils.ReadJSONData("db_Party.json");
        if (LoadingManager.ShouldYield())
        {
            yield return null;
        }
        parties = new Dictionary<string, PartyInfo>();
        Dictionary<string, object>[] array3 = data;
        foreach (Dictionary<string, object> dict3 in array3)
        {
            PartyInfo partyInfo = new PartyInfo
            {
                id = (string)dict3["ID"],
                gachaId = (string)dict3["SchemeName"],
                title = (string)dict3["Title"],
                description = (string)dict3["Description"]
            };
            parties.Add(partyInfo.id, partyInfo);
            if (LoadingManager.ShouldYield())
            {
                yield return null;
            }
        }
    }

    // =========================================================================
    // SOLUCIÓN DEFINITIVA: ROTACIÓN INFINITA MEDIANTE CICLO DE AÑOS (MÓDULO)
    // =========================================================================
    private string AjustarAnioGachaString(string dateStr, int offsetAnio)
    {
        if (string.IsNullOrEmpty(dateStr)) return dateStr;

        try
        {
            int primerGuion = dateStr.IndexOf('-');
            if (primerGuion != -1)
            {
                string anioOriginalStr = dateStr.Substring(0, primerGuion);
                int anioOriginal = int.Parse(anioOriginalStr);

                // 1. Definimos la duración total de tu rotación en el JSON.
                // Tus ejemplos van de 2021 a 2022, lo que equivale a un ciclo de 2 años.
                int tamanoCicloAnios = 2;
                int anioBaseJson = 2021;

                // 2. Corregimos el error tipográfico nativo del JSON (Diciembre 2022 -> Enero 2022)
                // Si el mes de inicio es diciembre y el de fin es enero, forzamos que el fin sea del año siguiente.
                if (offsetAnio == 1 && anioOriginal == anioBaseJson)
                {
                    // Caso normal de año nuevo dentro del ciclo
                }
                else if (dateStr.Contains("-01-29") && anioOriginal == 2022)
                {
                    // Parche específico para la línea corrupta del juego original:
                    // Tratamos el año de expiración como 2023 internamente para que la matemática no se rompa.
                    anioOriginal = 2023;
                }

                // 3. Calculamos la posición relativa de este cofre dentro de la rotación (0 o 1)
                int posicionEnCiclo = (anioOriginal - anioBaseJson) % tamanoCicloAnios;
                if (posicionEnCiclo < 0) posicionEnCiclo += tamanoCicloAnios;

                // 4. Calculamos en qué año del ciclo se encuentra el jugador hoy en día
                int anioActualPlataforma = DateTime.Now.Year;
                int cicloActual = (anioActualPlataforma - anioBaseJson) / tamanoCicloAnios;

                // 5. Proyectamos el año combinando el ciclo actual con la posición del cofre
                int anioFinal = anioBaseJson + (cicloActual * tamanoCicloAnios) + posicionEnCiclo + offsetAnio;

                // Si por desfases de calendario el evento calculado ya pasó este año, 
                // lo empujamos al siguiente ciclo para que siga disponible en el futuro inmediato
                if (anioFinal < anioActualPlataforma && offsetAnio == 0)
                {
                    anioFinal += tamanoCicloAnios;
                }

                string restoDeFecha = dateStr.Substring(primerGuion);
                return anioFinal.ToString() + restoDeFecha;
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning("Error en el ciclo infinito de Gacha: " + ex.Message);
        }

        return dateStr;
    }



    public void Destroy()
	{
		instance = null;
	}

	private GachaScheme GetCurrentScheme(bool forPremiumChest)
	{
		if (schemes == null)
		{
			return null;
		}
		DateTime now = TFUtils.ServerTime;
		return schemes.Find((GachaScheme s) => forPremiumChest == s.isPremium && now >= s.start && now <= s.end);
	}

	private void RecalculateWeights(bool forPremiumChest)
	{
		GachaScheme currentScheme = GetCurrentScheme(forPremiumChest);
		GachaWeightTable gachaWeightTable = new GachaWeightTable();
		foreach (KeyValuePair<string, Dictionary<string, int>> item in weightDict)
		{
			if (SQUtils.StartsWith(item.Key, "leader_") && LeaderManager.Instance.AlreadyOwned(item.Key))
			{
				continue;
			}
			int num = 0;
			foreach (string column in currentScheme.columns)
			{
				if (item.Value.ContainsKey(column))
				{
					num += item.Value[column];
				}
			}
			if (num > 0)
			{
				gachaWeightTable.Add(item.Key, num);
			}
		}
		if (forPremiumChest)
		{
			currPremiumWeights = gachaWeightTable;
		}
		else
		{
			currNormalWeights = gachaWeightTable;
		}
	}

	private void RecalculateWeightsForParty(string party, bool forPremiumChest)
	{
		GachaWeightTable gachaWeightTable = new GachaWeightTable();
		foreach (KeyValuePair<string, Dictionary<string, int>> item in weightDict)
		{
			if (!SQUtils.StartsWith(item.Key, "leader_") || !LeaderManager.Instance.AlreadyOwned(item.Key))
			{
				int num = (item.Value.ContainsKey(party) ? item.Value[party] : 0);
				if (num > 0)
				{
					gachaWeightTable.Add(item.Key, num);
				}
			}
		}
		if (forPremiumChest)
		{
			currPremiumWeights = gachaWeightTable;
		}
		else
		{
			currNormalWeights = gachaWeightTable;
		}
	}

	private void RecalculateWeightsForDailyGift(Quality aQuality)
	{
		string key = "DailyGift" + aQuality;
		GachaWeightTable gachaWeightTable = new GachaWeightTable();
		foreach (KeyValuePair<string, Dictionary<string, int>> item in weightDict)
		{
			if (!SQUtils.StartsWith(item.Key, "leader_") || !LeaderManager.Instance.AlreadyOwned(item.Key))
			{
				int num = (item.Value.ContainsKey(key) ? (num = item.Value[key]) : 0);
				if (num > 0)
				{
					gachaWeightTable.Add(item.Key, num);
				}
			}
		}
		switch (aQuality)
		{
		case Quality.Obsidian:
			DailyGiftObsidian = gachaWeightTable;
			break;
		case Quality.Gold:
			DailyGiftGold = gachaWeightTable;
			break;
		default:
			DailyGiftNormal = gachaWeightTable;
			break;
		}
	}

	public void GetChestCost(bool forPremiumChest, ref int coins, ref int gems)
	{
		GachaScheme currentScheme = GetCurrentScheme(forPremiumChest);
		if (currentScheme != null)
		{
			coins = currentScheme.coins;
			gems = currentScheme.gems;
		}
		else
		{
			coins = 0;
			gems = 0;
		}
	}

	public string PickDailyGift(Quality aQuality)
	{
		RecalculateWeightsForDailyGift(aQuality);
		switch (aQuality)
		{
		case Quality.Obsidian:
			return DailyGiftObsidian.Pick();
		case Quality.Gold:
			return DailyGiftGold.Pick();
		default:
			return DailyGiftNormal.Pick();
		}
	}

	public string PickColumn(string columnName)
	{
		GachaWeightTable gachaWeightTable = new GachaWeightTable();
		foreach (KeyValuePair<string, Dictionary<string, int>> item in weightDict)
		{
			if (!SQUtils.StartsWith(item.Key, "leader_") || !LeaderManager.Instance.AlreadyOwned(item.Key))
			{
				int num = (item.Value.ContainsKey(columnName) ? item.Value[columnName] : 0);
				if (num > 0)
				{
					gachaWeightTable.Add(item.Key, num);
				}
			}
		}
		return gachaWeightTable.Pick();
	}

	public string PickPremium()
	{
		RecalculateWeights(true);
		string text = currPremiumWeights.Pick();
		if (text == null)
		{
			string text2 = "badpremgatcha";
			string text3 = "Bad premium gatcha - scheme: ";
			GachaScheme currentScheme = GetCurrentScheme(true);
			if (currentScheme != null)
			{
				foreach (string column in currentScheme.columns)
				{
					text3 = text3 + column + " ";
					text2 = text2 + "_" + column;
				}
			}
			else
			{
				text3 += "null ";
			}
			GachaWeightTable gachaWeightTable = currPremiumWeights;
			string text4 = text3;
			text3 = text4 + "weight: " + gachaWeightTable.totalWeight + " [" + gachaWeightTable.weights.Count + "] ";
			if (gachaWeightTable.weights.Count > 0)
			{
				text3 = text3 + "last weight: " + gachaWeightTable.weights[gachaWeightTable.weights.Count - 1].weight;
			}
			CrashAnalytics.LogException(new Exception(text3));
			Singleton<AnalyticsManager>.Instance.LogDebug(text2, gachaWeightTable.weights.Count);
			CrashAnalytics.LogBreadcrumb("Null premium gacha replace");
		}
		if (text == null)
		{
			text = PickNormal();
		}
		return text;
	}

	public string PickPremium(string party)
	{
		RecalculateWeightsForParty(party, true);
		string text = currPremiumWeights.Pick();
		if (text == null)
		{
			string text2 = "badpremgatcha_party" + party;
			string text3 = "Bad party premium gatcha - party: " + party + " scheme: ";
			GachaScheme currentScheme = GetCurrentScheme(true);
			if (currentScheme != null)
			{
				foreach (string column in currentScheme.columns)
				{
					text3 = text3 + column + " ";
					text2 = text2 + "_" + column;
				}
			}
			else
			{
				text3 += "null ";
			}
			GachaWeightTable gachaWeightTable = currPremiumWeights;
			string text4 = text3;
			text3 = text4 + "weight: " + gachaWeightTable.totalWeight + " [" + gachaWeightTable.weights.Count + "] ";
			if (gachaWeightTable.weights.Count > 0)
			{
				text3 = text3 + "last weight: " + gachaWeightTable.weights[gachaWeightTable.weights.Count - 1].weight;
			}
			CrashAnalytics.LogException(new Exception(text3));
			Singleton<AnalyticsManager>.Instance.LogDebug(text2, gachaWeightTable.weights.Count);
			CrashAnalytics.LogBreadcrumb("Null premium party gacha replace");
		}
		if (text == null)
		{
			text = PickNormal();
		}
		return text;
	}

	public string PickNormal()
	{
		RecalculateWeights(false);
		string text = currNormalWeights.Pick();
		if (text == null)
		{
			string text2 = "badnormgatcha";
			string text3 = "Bad normal gatcha - scheme: ";
			GachaScheme currentScheme = GetCurrentScheme(true);
			if (currentScheme != null)
			{
				foreach (string column in currentScheme.columns)
				{
					text3 = text3 + column + " ";
					text2 = text2 + "_" + column;
				}
			}
			else
			{
				text3 += "null ";
			}
			GachaWeightTable gachaWeightTable = currNormalWeights;
			string text4 = text3;
			text3 = text4 + "weight: " + gachaWeightTable.totalWeight + " [" + gachaWeightTable.weights.Count + "] ";
			if (gachaWeightTable.weights.Count > 0)
			{
				text3 = text3 + "last weight: " + gachaWeightTable.weights[gachaWeightTable.weights.Count - 1].weight;
			}
			CrashAnalytics.LogException(new Exception(text3));
			Singleton<AnalyticsManager>.Instance.LogDebug(text2, gachaWeightTable.weights.Count);
			CrashAnalytics.LogBreadcrumb("Null premium party gacha replace");
		}
		return text;
	}

	public PartyInfo GetCurrentPartyInfo()
	{
		GachaScheme currentScheme = GetCurrentScheme(true);
		if (currentScheme != null && currentScheme.party != null)
		{
			if (!parties.ContainsKey(currentScheme.party))
			{
				return null;
			}
			PartyInfo partyInfo = parties[currentScheme.party];
			partyInfo.end = currentScheme.end;
			return partyInfo;
		}
		return null;
	}
}
