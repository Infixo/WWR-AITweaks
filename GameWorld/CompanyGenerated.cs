using HarmonyLib;
using Microsoft.Xna.Framework;
using STM.Data;
using STM.GameWorld;
using STVisual.Utility;
using static STM.GameWorld.CompanyGenerated; // CompanyType
using Utilities;

namespace AITweaks.GameWorld;


[HarmonyPatch(typeof(CompanyGenerated))]
public static class CompanyGenerated_Patches
{
    [HarmonyPatch("Generate"), HarmonyPrefix]
    public static bool CompanyGenerated_Generate_Prefix(CompanyGenerated __instance, ref CompanyGenerated __result, Session session, Country country, CompanyType? company_type = null)
    {
        CHash16Bit _hash = new CHash16Bit(session.Companies.Count + session.GetSeed("c", 1024));
        //bool noCountry = country == null;
        country = country ?? typeof(CompanyGenerated).CallPrivateStaticMethod<Country>("GetCountry", [_hash, session]);
        _hash = new CHash16Bit(session.Companies.Count + session.GetSeed(country.Name.Original, 2048) + _hash.NextInt());
        int _logo = _hash.NextInt() % MainData.Logos_back.Length;
        Color _color_main = Color.White;
        if (_hash.NextFloat() > 0.3f)
        {
            _color_main = GetRandomColor(240);
        }
        string _sub = null!;
        if (_hash.NextFloat(0f, 1f) > 0.5f)
        {
            _sub = Localization.Company_name_gen_input.Values[_hash.NextInt(0, Localization.Company_name_gen_input.Values.Count - 1)].name;
        }
        // There is a problem with GetColor - it does not exist
        // Probably there should some conversion involved
        // Commenting out atm, not needed
        /*
        if (noCountry && _hash.NextFloat(0f, 1f) > 0.975f)
        {
            DefValueArray _info = PastPlays.GetPastPlay(session, _hash);
            if (_info != null)
            {
                try
                {
                    string _name5 = _info.Name;
                    _color_main = _info.GetColor("color_main", _color_main);
                    Color _color_secondary = _info.GetColor("color_secondary", typeof(CompanyGenerated).CallPrivateStaticMethod<Color>("GetRandomColor", [_hash, 200]));
                    _logo = MainData.GetLogo(_info.GetString("icon", MainData.Logos_front[_logo].Name));
                    country = session.Scene.GetCountry(_info.GetString("country", "LT"));
                    if (company_type.HasValue)
                    {
                        return new CompanyGenerated(country, "", _name5, company_type.Value, _color_main, _color_secondary, _logo);
                    }
                    return new CompanyGenerated(country, "", _name5, CompanyType.All, _color_main, _color_secondary, _logo);
                }
                catch
                {
                }
            }
        }
        */

        // Patch 1.1.15 - any vehicle combination can be set when starting a game
        CompanyType _allowed = (CompanyType)session.Scene.Settings.Vehicles_flag;

        // Type.ALL
        if (company_type.HasValue)
        {
            if (company_type.Value == CompanyType.All)
            {
                company_type = _allowed;
                if (_allowed == CompanyType.All && _hash.NextInt() % 100 < 25)
                {
                    int _type = (int)company_type.Value;
                    int _mask = 1 << (_hash.NextInt() % 4);
                    _type ^= _mask;
                    company_type = (CompanyType)_type;
                }
            }
            __result = GetCompany(company_type.Value);
            return false;
        }

        // Randomly generated company type
        company_type = 0;
        int _tries = 10; // Limit random rolls in case of some stupid RNG
        while (company_type.Value == 0 && _tries-- > 0)
        {
            int _h = _hash.NextInt() % 100;
            if (_h > 83)
                company_type = CompanyType.Ships | CompanyType.Planes;
            else if (_h > 66)
                company_type = CompanyType.Ships;
            else if (_h > 50)
                company_type = CompanyType.Planes;
            else if (_h > 33)
                company_type = CompanyType.Trains | CompanyType.Road_vehicles;
            else if (_h > 16)
                company_type = CompanyType.Trains; // It looks like starting with trains only is simply impossible for AI, rails are too expensive?
            else
                company_type = CompanyType.Road_vehicles;
            company_type &= _allowed;
        }
        __result = GetCompany(_tries > 0 ? company_type.Value : _allowed);
        return false;

        // Helpers
        Color GetRandomColor(byte max)
        {
            return typeof(CompanyGenerated).CallPrivateStaticMethod<Color>("GetRandomColor", [_hash, max]);
        }

        CompanyGenerated GetCompany(CompanyType type)
        {
            string _name = GetName(_hash, country, session, type, ref _sub);
            return new CompanyGenerated(country, _name, _sub, type, _color_main, GetRandomColor(200), _logo);
        }
    }


    internal static string GetName(CHash16Bit hash, Country country, Session session, CompanyType type, ref string sub)
    {
        int _names = Localization.Company_name_gen.Values.Count / 5;
        int _offset = type switch
        {
            CompanyType.Road_vehicles => 0,
            CompanyType.Trains => _names,
            CompanyType.Road_vehicles|CompanyType.Trains => _names,
            CompanyType.Planes => _names * 2,
            CompanyType.Ships => _names * 3,
            CompanyType.Planes|CompanyType.Ships => _names * (2 + (hash.NextInt() & 1)),
            _ => _names * 4,
        };
        int _name_id = hash.NextInt() % _names;
        string _name = Localization.Company_name_gen.Values[_offset + _name_id].name;
        int _tries = _names;
        if (sub == null)
        {
            while (!NameCountryValid(_name, country, session))
            {
                if (_tries <= 0)
                {
                    _tries--;
                    break;
                }
                _name_id = (_name_id + 1) % _names;
                _name = Localization.Company_name_gen.Values[_offset + _name_id].name;
                _tries--;
            }
            if (_tries >= 0)
            {
                return _name;
            }
        }
        _names = Localization.Company_name_gen_input.Values.Count;
        _name_id = hash.NextInt() % _names;
        sub = Localization.Company_name_gen_input.Values[_name_id].name;
        _tries = Localization.Company_name_gen_input.Values.Count;
        while (!NameSubnameValid(_name, sub, session))
        {
            if (_tries <= 0)
            {
                _name = _name + " - " + session.Companies.Count;
                break;
            }
            _name_id = (_name_id + 1) % _names;
            sub = Localization.Company_name_gen_input.Values[_name_id].name;
            _tries--;
        }
        return _name;

        // Helpers
        bool NameCountryValid(string name, Country country, Session session)
        {
            for (int i = 0; i < session.Companies.Count; i++)
                if (session.Companies[i].Info is CompanyGenerated _generated && _generated.GetPrivateField<string>("name_gen") == name && session.Companies[i].Info.GetCountry(session.Scene) == country)
                    return false;
            return true;
        }
        bool NameSubnameValid(string name, string sub, Session session)
        {
            for (int i = 0; i < session.Companies.Count; i++)
                if (session.Companies[i].Info is CompanyGenerated _generated && _generated.GetPrivateField<string>("name_gen") == name && _generated.GetPrivateField<string>("sub_company") == sub)
                    return false;
            return true;
        }
    }

    private static void TestRandom(CHash16Bit hash)
    {
        int[] freq = new int[20];
        for (int i = 0; i < 1000; i++)
            freq[hash.NextInt() % 20]++;
        for (int i = 0; i <  freq.Length; i++)
            Log.Write($"{i}: {freq[i]}");
    }

    // For debug and testing purposes
    //[HarmonyPatch(typeof(Session), "GetCompanies"), HarmonyPostfix]
    public static void Session_GetCompanies(Session __instance)
    {
        void GetCompany(CompanyType type, ref int _tot, float prob)
        {
            int _count = 0;
            for (int i = 0; i < __instance.Companies.Count; i++)
                if (__instance.Companies[i].Info is CompanyGenerated _comp && _comp.Company_type == type)
                    _count++;
            Log.Write($"{_count} ({prob*(float)(__instance.Companies.Count-1):F1}) {type}");
            _tot += _count;
        }
        int _tot = 0;
        GetCompany(CompanyType.All, ref _tot, 0.5f * 0.75f);
        GetCompany(CompanyType.Road_vehicles, ref _tot, 0.1f);
        GetCompany(CompanyType.Trains, ref _tot, 0); // It looks like starting with trains only is simply impossible for AI, rails are too expensive?
        GetCompany(CompanyType.Ships, ref _tot, 0.1f);
        GetCompany(CompanyType.Planes, ref _tot, 0.1f);
        GetCompany(CompanyType.Trains | CompanyType.Road_vehicles, ref _tot, 0.1f);
        GetCompany(CompanyType.Ships | CompanyType.Planes, ref _tot, 0.1f);
        Log.Write($"{__instance.Companies.Count - 1 - _tot} ({0.5f*0.25f* (float)(__instance.Companies.Count - 1):F1}) 3-types");
    }
}
