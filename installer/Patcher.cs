// Oxenfree 한글패치 핵심 로직 (화면과 분리)
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

static class Patcher
{
    public const string Version = "1.0";
    const int LangKorean = 23;   // UnityEngine.SystemLanguage.Korean
    const int LangRussian = 30;  // 드롭다운의 러시아어 칸을 한국어로 사용
    const string MarkerType = "__OxenfreeKoPatch_ByKimJuke";
    const string HelperName = "__KoFixDropdown";
    public const string BackupExt = ".ko_backup";

    public static Action<string> Log = s => { };

    public static string DllPath(string gameDir) { return Path.Combine(gameDir, "Oxenfree_Data", "Managed", "Assembly-CSharp.dll"); }

    public static bool IsGameDir(string d)
    {
        try { return !string.IsNullOrEmpty(d) && File.Exists(DllPath(d)); } catch { return false; }
    }

    // 게임 폴더 자동 탐색: 지정 경로 → 실행 위치 → 스팀 라이브러리
    public static string FindGameDir(string hint)
    {
        var cands = new List<string>();
        if (!string.IsNullOrEmpty(hint)) cands.Add(hint);
        cands.Add(AppDomain.CurrentDomain.BaseDirectory);
        cands.Add(Directory.GetCurrentDirectory());
        foreach (var c0 in cands)
        {
            try
            {
                var c = Path.GetFullPath(c0);
                for (int i = 0; i < 4 && c != null; i++)
                {
                    if (IsGameDir(c)) return c;
                    c = Path.GetDirectoryName(c);
                }
            }
            catch { }
        }
        foreach (var lib in SteamLibraries())
        {
            var d = Path.Combine(lib, "steamapps", "common", "Oxenfree");
            if (IsGameDir(d)) return d;
        }
        return null;
    }

    static IEnumerable<string> SteamLibraries()
    {
        var roots = new List<string>();
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                if (k != null && k.GetValue("SteamPath") != null) roots.Add(k.GetValue("SteamPath").ToString().Replace('/', '\\'));
        }
        catch { }
        try
        {
            using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
                if (k != null && k.GetValue("InstallPath") != null) roots.Add(k.GetValue("InstallPath").ToString());
        }
        catch { }
        var libs = new List<string>(roots);
        foreach (var r in roots)
        {
            try
            {
                var vdf = Path.Combine(r, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                    libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
            }
            catch { }
        }
        return libs.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    // 패치는 됐지만 최신 기능(자막 바탕 없애기)이 빠진 상태
    public static bool NeedsUpdate(string gameDir) { return IsPatched(gameDir) && !HasBytes(gameDir, SubHelper); }

    // Cecil 없이 표식 문자열만 찾아 빠르게 확인
    public static bool IsPatched(string gameDir) { return HasBytes(gameDir, MarkerType); }

    static bool HasBytes(string gameDir, string marker)
    {
        try
        {
            var b = File.ReadAllBytes(DllPath(gameDir));
            var m = Encoding.ASCII.GetBytes(marker);
            for (int i = 0; i <= b.Length - m.Length; i++)
            {
                if (b[i] != m[0]) continue;
                int j = 1; while (j < m.Length && b[i + j] == m[j]) j++;
                if (j == m.Length) return true;
            }
        }
        catch { }
        return false;
    }

    public static bool IsGameRunning()
    {
        try { return System.Diagnostics.Process.GetProcessesByName("OXENFREE").Length > 0; } catch { return false; }
    }

    // ---------------------------------------------------------------- 설치
    public static bool Install(string gameDir)
    {
        string dll = DllPath(gameDir), bak = dll + BackupExt;
        if (IsGameRunning()) { Log("! 게임이 실행 중입니다. 게임을 끄고 다시 시도하세요."); return false; }

        byte[] orig = File.ReadAllBytes(dll);
        ModuleDefinition mod = ReadModule(orig, gameDir);
        if (mod.Types.Any(t => t.Name == MarkerType))
        {
            if (!HasSubtitlePatch(mod))
            {
                PatchSubtitle(mod);
                var ms0 = new MemoryStream(); mod.Write(ms0); File.WriteAllBytes(dll, ms0.ToArray());
                Log("자막 바탕 없애기 추가 적용");
            }
            else Log("이미 한글패치가 적용되어 있습니다.");
            SetKoreanLanguage(gameDir, FindKoreanIndex(mod));
            return true;
        }

        File.WriteAllBytes(bak, orig);
        Log("1/4 원본 백업 완료");

        TypeDefinition dialog = mod.GetType("Dialog");
        if (dialog == null) throw new Exception("Dialog 클래스를 찾지 못했습니다.");
        FieldDefinition supported = dialog.Fields.FirstOrDefault(f => f.Name == "supportedLanguages");
        MethodDefinition cctor = dialog.Methods.FirstOrDefault(m => m.Name == ".cctor");
        if (supported == null || cctor == null) throw new Exception("언어 목록을 찾지 못했습니다.");

        var ins = cctor.Body.Instructions;
        int store = -1;
        for (int i = 0; i < ins.Count; i++)
            if (ins[i].OpCode == OpCodes.Stsfld && ((FieldReference)ins[i].Operand).Name == "supportedLanguages") { store = i; break; }
        if (store < 0) throw new Exception("언어 목록 초기화 코드를 찾지 못했습니다.");
        int listStart = store;
        while (listStart > 0 && ins[listStart].OpCode != OpCodes.Newobj) listStart--;

        var langs = new List<int>(); var langIns = new List<Instruction>();
        for (int i = listStart; i < store; i++)
        {
            int v;
            if (TryGetInt(ins[i], out v) && i + 1 < store && ins[i + 1].OpCode == OpCodes.Callvirt
                && ((MethodReference)ins[i + 1].Operand).Name == "Add") { langs.Add(v); langIns.Add(ins[i]); }
        }
        int ru = langs.IndexOf(LangRussian);
        if (ru < 0)
        {
            if (langs.IndexOf(LangKorean) < 0) throw new Exception("이 게임 버전은 지원하지 않습니다.");
            Log("2/4 기존 한국어 칸 사용");
        }
        else
        {
            var il = cctor.Body.GetILProcessor();
            il.Replace(langIns[ru], il.Create(OpCodes.Ldc_I4_S, (sbyte)LangKorean));
            langs[ru] = LangKorean;
            Log("2/4 언어 목록: 러시아어 칸 → 한국어");
        }
        int koIndex = langs.IndexOf(LangKorean);

        MethodDefinition helper = MakeDropdownHelper(mod, dialog, supported);
        int hooked = 0;
        foreach (var tn in new[] { "MainMenu", "PauseMenu" })
        {
            var t = mod.GetType(tn);
            if (t == null) continue;
            var fld = t.Fields.FirstOrDefault(f => f.Name == "langDropdown");
            if (fld == null) continue;
            foreach (var m in t.Methods.Where(x => x.HasBody))
            {
                var b = m.Body.Instructions;
                for (int i = 1; i < b.Count; i++)
                {
                    var mr = b[i].Operand as MethodReference;
                    var prev = b[i - 1].Operand as MethodReference;
                    if (b[i].OpCode == OpCodes.Callvirt && mr != null && mr.Name == "set_value" && mr.DeclaringType.Name == "Dropdown"
                        && prev != null && prev.Name == "GetTextLanguageIndex")
                    {
                        var p = m.Body.GetILProcessor();
                        var a1 = p.Create(OpCodes.Ldarg_0); var a2 = p.Create(OpCodes.Ldfld, fld); var a3 = p.Create(OpCodes.Call, helper);
                        p.InsertAfter(b[i], a1); p.InsertAfter(a1, a2); p.InsertAfter(a2, a3);
                        hooked++; i += 3;
                    }
                }
            }
        }
        Log("3/4 메뉴에 '한국어' 표시 추가 (" + hooked + "곳)");
        PatchSubtitle(mod);
        Log("    자막 바탕 없애고 글씨 테두리 추가");

        mod.Types.Add(new TypeDefinition("", MarkerType, TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed, mod.TypeSystem.Object));
        var ms = new MemoryStream();
        mod.Write(ms);
        File.WriteAllBytes(dll, ms.ToArray());
        Log("4/4 Assembly-CSharp.dll 저장");

        SetKoreanLanguage(gameDir, koIndex);
        return true;
    }

    // ---------------------------------------------------------------- 자막: 검은 바탕 없애고 글씨만(테두리)
    const string SubHelper = "__KoSubTextOnly", OutlineHelper = "__KoTextOutline";

    static bool HasSubtitlePatch(ModuleDefinition mod)
    {
        var d = mod.GetType("Dialog");
        return d != null && d.Methods.Any(m => m.Name == SubHelper);
    }

    static TypeDefinition BaseOf(TypeDefinition t, string fullName)
    {
        while (t != null && t.FullName != fullName) t = t.BaseType == null ? null : t.BaseType.Resolve();
        return t;
    }

    static void PatchSubtitle(ModuleDefinition mod)
    {
        var sub = mod.GetType("Subtitle");
        var dialog = mod.GetType("Dialog");
        if (sub == null) throw new Exception("Subtitle 클래스를 찾지 못했습니다.");
        var fText = sub.Fields.First(f => f.Name == "text");
        var fChar = sub.Fields.FirstOrDefault(f => f.Name == "characterText");
        var awake = sub.Methods.First(m => m.Name == "Awake" && m.HasBody);

        var textDef = fText.FieldType.Resolve();
        var uiMod = textDef.Module;
        var imageDef = uiMod.GetType("UnityEngine.UI.Image");
        var outlineDef = uiMod.GetType("UnityEngine.UI.Outline");
        var shadowDef = BaseOf(outlineDef, "UnityEngine.UI.Shadow");
        var compDef = BaseOf(sub.BaseType.Resolve(), "UnityEngine.Component");
        var behDef = BaseOf(sub.BaseType.Resolve(), "UnityEngine.Behaviour");
        var objDef = BaseOf(compDef, "UnityEngine.Object");
        var coreMod = compDef.Module;
        var colorDef = coreMod.GetType("UnityEngine.Color");
        var vec2Def = coreMod.GetType("UnityEngine.Vector2");
        var goGetter = compDef.Methods.First(m => m.Name == "get_gameObject");
        var goDef = goGetter.ReturnType.Resolve();

        var getChildren = new GenericInstanceMethod(mod.ImportReference(compDef.Methods.First(m =>
            m.Name == "GetComponentsInChildren" && m.HasGenericParameters && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.Boolean")));
        getChildren.GenericArguments.Add(mod.ImportReference(imageDef));
        var addComp = new GenericInstanceMethod(mod.ImportReference(goDef.Methods.First(m =>
            m.Name == "AddComponent" && m.HasGenericParameters && m.Parameters.Count == 0)));
        addComp.GenericArguments.Add(mod.ImportReference(outlineDef));
        var setEnabled = mod.ImportReference(behDef.Methods.First(m => m.Name == "set_enabled"));
        var getGo = mod.ImportReference(goGetter);
        var setColor = mod.ImportReference(shadowDef.Methods.First(m => m.Name == "set_effectColor"));
        var setDist = mod.ImportReference(shadowDef.Methods.First(m => m.Name == "set_effectDistance"));
        var colorCtor = mod.ImportReference(colorDef.Methods.First(m => m.IsConstructor && m.Parameters.Count == 4));
        var vecCtor = mod.ImportReference(vec2Def.Methods.First(m => m.IsConstructor && m.Parameters.Count == 2));
        var opEq = mod.ImportReference(objDef.Methods.First(m => m.Name == "op_Equality"));
        var textRef = mod.ImportReference(textDef);
        var outlineRef = mod.ImportReference(outlineDef);
        var imageArr = new ArrayType(mod.ImportReference(imageDef));

        // static void __KoTextOutline(Text t): 검은 테두리
        var h1 = new MethodDefinition(OutlineHelper, MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, mod.TypeSystem.Void);
        h1.Parameters.Add(new ParameterDefinition("t", ParameterAttributes.None, textRef));
        h1.Body.Variables.Add(new VariableDefinition(outlineRef)); h1.Body.InitLocals = true;
        var p = h1.Body.GetILProcessor(); var r1 = p.Create(OpCodes.Ret);
        p.Emit(OpCodes.Ldarg_0); p.Emit(OpCodes.Ldnull); p.Emit(OpCodes.Call, opEq); p.Emit(OpCodes.Brtrue, r1);
        p.Emit(OpCodes.Ldarg_0); p.Emit(OpCodes.Callvirt, getGo); p.Emit(OpCodes.Callvirt, addComp); p.Emit(OpCodes.Stloc_0);
        p.Emit(OpCodes.Ldloc_0); p.Emit(OpCodes.Ldc_R4, 0f); p.Emit(OpCodes.Ldc_R4, 0f); p.Emit(OpCodes.Ldc_R4, 0f); p.Emit(OpCodes.Ldc_R4, 0.9f);
        p.Emit(OpCodes.Newobj, colorCtor); p.Emit(OpCodes.Callvirt, setColor);
        p.Emit(OpCodes.Ldloc_0); p.Emit(OpCodes.Ldc_R4, 1.6f); p.Emit(OpCodes.Ldc_R4, -1.6f);
        p.Emit(OpCodes.Newobj, vecCtor); p.Emit(OpCodes.Callvirt, setDist);
        p.Append(r1);
        dialog.Methods.Add(h1);

        // static void __KoSubTextOnly(Subtitle s): 자막 안의 이미지(바탕) 끄기 + 글씨 테두리
        var h2 = new MethodDefinition(SubHelper, MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, mod.TypeSystem.Void);
        h2.Parameters.Add(new ParameterDefinition("s", ParameterAttributes.None, sub));
        h2.Body.Variables.Add(new VariableDefinition(imageArr));
        h2.Body.Variables.Add(new VariableDefinition(mod.TypeSystem.Int32));
        h2.Body.InitLocals = true;
        p = h2.Body.GetILProcessor();
        var cond = p.Create(OpCodes.Ldloc_1); var body = p.Create(OpCodes.Ldloc_0);
        p.Emit(OpCodes.Ldarg_0); p.Emit(OpCodes.Ldc_I4_1); p.Emit(OpCodes.Callvirt, getChildren); p.Emit(OpCodes.Stloc_0);
        p.Emit(OpCodes.Ldc_I4_0); p.Emit(OpCodes.Stloc_1); p.Emit(OpCodes.Br, cond);
        p.Append(body); p.Emit(OpCodes.Ldloc_1); p.Emit(OpCodes.Ldelem_Ref); p.Emit(OpCodes.Ldc_I4_0); p.Emit(OpCodes.Callvirt, setEnabled);
        p.Emit(OpCodes.Ldloc_1); p.Emit(OpCodes.Ldc_I4_1); p.Emit(OpCodes.Add); p.Emit(OpCodes.Stloc_1);
        p.Append(cond); p.Emit(OpCodes.Ldloc_0); p.Emit(OpCodes.Ldlen); p.Emit(OpCodes.Conv_I4); p.Emit(OpCodes.Blt, body);
        p.Emit(OpCodes.Ldarg_0); p.Emit(OpCodes.Ldfld, fText); p.Emit(OpCodes.Call, h1);
        if (fChar != null) { p.Emit(OpCodes.Ldarg_0); p.Emit(OpCodes.Ldfld, fChar); p.Emit(OpCodes.Call, h1); }
        p.Emit(OpCodes.Ret);
        dialog.Methods.Add(h2);

        // Subtitle.Awake 맨 앞에서 호출
        var ap = awake.Body.GetILProcessor();
        var first = awake.Body.Instructions[0];
        ap.InsertBefore(first, ap.Create(OpCodes.Ldarg_0));
        ap.InsertBefore(first, ap.Create(OpCodes.Call, h2));
    }

    static int FindKoreanIndex(ModuleDefinition mod)
    {
        var cctor = mod.GetType("Dialog").Methods.First(m => m.Name == ".cctor");
        var ins = cctor.Body.Instructions; int idx = 0;
        for (int i = 0; i < ins.Count; i++)
        {
            if (ins[i].OpCode == OpCodes.Stsfld && ((FieldReference)ins[i].Operand).Name == "supportedLanguages") break;
            int v;
            if (TryGetInt(ins[i], out v) && i + 1 < ins.Count && ins[i + 1].OpCode == OpCodes.Callvirt
                && ((MethodReference)ins[i + 1].Operand).Name == "Add")
            { if (v == LangKorean) return idx; idx++; }
        }
        return -1;
    }

    static bool TryGetInt(Instruction i, out int v)
    {
        v = 0;
        var c = i.OpCode.Code;
        if (c >= Code.Ldc_I4_0 && c <= Code.Ldc_I4_8) { v = c - Code.Ldc_I4_0; return true; }
        if (c == Code.Ldc_I4_M1) { v = -1; return true; }
        if (c == Code.Ldc_I4_S) { v = (sbyte)i.Operand; return true; }
        if (c == Code.Ldc_I4) { v = (int)i.Operand; return true; }
        return false;
    }

    // static void __KoFixDropdown(Dropdown d): 드롭다운의 한국어 칸 글자를 "한국어"로
    static MethodDefinition MakeDropdownHelper(ModuleDefinition mod, TypeDefinition dialog, FieldDefinition supported)
    {
        MethodReference setValue = FindRef(mod, r => r.Name == "set_value" && r.DeclaringType.Name == "Dropdown");
        MethodReference opEq = FindRef(mod, r => r.Name == "op_Equality" && r.DeclaringType.FullName == "UnityEngine.Object");
        MethodReference indexOf = FindRef(mod, r => r.Name == "IndexOf" && r.DeclaringType.FullName.Contains("SystemLanguage"));
        if (setValue == null || opEq == null || indexOf == null) throw new Exception("필요한 코드 참조를 찾지 못했습니다.");

        TypeReference ddRef = setValue.DeclaringType;
        TypeDefinition ddDef = ddRef.Resolve();
        if (ddDef == null) throw new Exception("UnityEngine.UI.dll을 읽지 못했습니다.");
        MethodReference getOptions = mod.ImportReference(ddDef.Methods.First(m => m.Name == "get_options" && !m.HasParameters));
        MethodReference refresh = mod.ImportReference(ddDef.Methods.First(m => m.Name == "RefreshShownValue"));
        TypeDefinition optDef = ddDef.NestedTypes.First(t => t.Name == "OptionData");
        MethodReference setText = mod.ImportReference(optDef.Methods.First(m => m.Name == "set_text"));

        var listInst = (GenericInstanceType)getOptions.ReturnType;
        var listDef = listInst.ElementType.Resolve();
        var getCount = new MethodReference("get_Count", mod.TypeSystem.Int32, listInst) { HasThis = true };
        var getItem = new MethodReference("get_Item", listDef.GenericParameters[0], listInst) { HasThis = true };
        getItem.Parameters.Add(new ParameterDefinition(mod.TypeSystem.Int32));

        var m2 = new MethodDefinition(HelperName, MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, mod.TypeSystem.Void);
        m2.Parameters.Add(new ParameterDefinition("d", ParameterAttributes.None, ddRef));
        m2.Body.Variables.Add(new VariableDefinition(mod.TypeSystem.Int32));
        m2.Body.InitLocals = true;
        var p = m2.Body.GetILProcessor();
        var ret = p.Create(OpCodes.Ret);
        p.Emit(OpCodes.Ldarg_0); p.Emit(OpCodes.Ldnull); p.Emit(OpCodes.Call, opEq); p.Emit(OpCodes.Brtrue, ret);
        p.Emit(OpCodes.Ldsfld, supported); p.Emit(OpCodes.Ldc_I4_S, (sbyte)LangKorean); p.Emit(OpCodes.Callvirt, indexOf); p.Emit(OpCodes.Stloc_0);
        p.Emit(OpCodes.Ldloc_0); p.Emit(OpCodes.Ldc_I4_0); p.Emit(OpCodes.Blt, ret);
        p.Emit(OpCodes.Ldloc_0); p.Emit(OpCodes.Ldarg_0); p.Emit(OpCodes.Callvirt, getOptions); p.Emit(OpCodes.Callvirt, getCount); p.Emit(OpCodes.Bge, ret);
        p.Emit(OpCodes.Ldarg_0); p.Emit(OpCodes.Callvirt, getOptions); p.Emit(OpCodes.Ldloc_0); p.Emit(OpCodes.Callvirt, getItem);
        p.Emit(OpCodes.Ldstr, "한국어"); p.Emit(OpCodes.Callvirt, setText);
        p.Emit(OpCodes.Ldarg_0); p.Emit(OpCodes.Callvirt, refresh);
        p.Append(ret);
        dialog.Methods.Add(m2);
        return m2;
    }

    static MethodReference FindRef(ModuleDefinition mod, Func<MethodReference, bool> pred)
    {
        foreach (var t in mod.GetTypes())
            foreach (var m in t.Methods)
                if (m.HasBody)
                    foreach (var i in m.Body.Instructions)
                    {
                        var r = i.Operand as MethodReference;
                        if (r != null && pred(r)) return r;
                    }
        return null;
    }

    static ModuleDefinition ReadModule(byte[] bytes, string gameDir)
    {
        var res = new DefaultAssemblyResolver();
        res.AddSearchDirectory(Path.Combine(gameDir, "Oxenfree_Data", "Managed"));
        return ModuleDefinition.ReadModule(new MemoryStream(bytes), new ReaderParameters { AssemblyResolver = res, ReadingMode = ReadingMode.Immediate });
    }

    // ---------------------------------------------------------------- 복원
    public static bool Restore(string gameDir)
    {
        string dll = DllPath(gameDir), bak = dll + BackupExt;
        if (IsGameRunning()) { Log("! 게임이 실행 중입니다. 게임을 끄고 다시 시도하세요."); return false; }
        if (!IsPatched(gameDir)) { Log("패치되어 있지 않습니다. (복원 불필요)"); return true; }
        if (!File.Exists(bak))
        {
            Log("! 백업 파일이 없습니다.");
            Log("스팀 > 속성 > 게임 파일 무결성 확인을 해 주세요.");
            return false;
        }
        File.Copy(bak, dll, true);
        Log("원본 Assembly-CSharp.dll로 복원했습니다.");
        return true;
    }

    // ---------------------------------------------------------------- 언어 설정(PlayerPrefs = 레지스트리)
    static void SetKoreanLanguage(string gameDir, int index)
    {
        if (index < 0 || Environment.OSVersion.Platform != PlatformID.Win32NT) return;
        try
        {
            string company = "Night School Studio", product = "OXENFREE";
            var info = Path.Combine(gameDir, "Oxenfree_Data", "app.info");
            if (File.Exists(info))
            {
                var lines = File.ReadAllLines(info);
                if (lines.Length >= 2) { company = lines[0].Trim(); product = lines[1].Trim(); }
            }
            string name = "text_language";
            uint h = 5381;
            foreach (char c in name) h = (h * 33) ^ c;
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Software\\" + company + "\\" + product))
                key.SetValue(name + "_h" + h, index, Microsoft.Win32.RegistryValueKind.DWord);
            Log("게임 언어를 한국어로 지정");
        }
        catch (Exception e)
        {
            Log("언어 자동 지정 실패: 게임 메뉴에서 '한국어' 선택");
            Log("(" + e.Message + ")");
        }
    }
}
