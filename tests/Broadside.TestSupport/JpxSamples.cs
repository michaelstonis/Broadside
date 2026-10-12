namespace Broadside.TestSupport;

/// <summary>
/// JPEG 2000 codestreams for the JPXDecode tests, fuzz seeds and benchmarks (ITU-T T.800 | ISO/IEC 15444-1). Expected samples come
/// from an independent source: the worked example of T.800 J.11, or the deterministic source image an OpenJPEG-encoded vector was
/// compressed from losslessly (<see cref="Sample"/>).
/// </summary>
public static class JpxSamples
{
    /// <summary>
    /// The 100-byte codestream of ITU-T T.800 J.11 (T-REC-T.800-200208, printed pages 173 to 177): one 1 x 9 component of 8-bit
    /// unsigned samples, one tile, one level of the reversible 5/3 transform, LRCP, one layer.
    /// </summary>
    public static byte[] WorkedExample { get; } = Convert.FromHexString(
        "FF4FFF510029000000000001000000090000000000000000000000010000000900000000000000000001070101FF5C00074040484850" +
        "FF52000C00000001000104040001FF90000A00000000001E0001FF93C7D40C018F0DC8755DC07C21800FB176FFD9");

    /// <summary>The samples J.11.5 lists for <see cref="WorkedExample"/>, top to bottom.</summary>
    public static byte[] WorkedExampleSamples { get; } = [101, 103, 104, 105, 96, 97, 96, 102, 109];

    /// <summary>
    /// Codestreams OpenJPEG 2.5.4 (<c>opj_compress</c>, the command in each entry) encoded from <see cref="Sample"/>: every one is
    /// lossless except <c>Irreversible</c> (9/7), so its samples are exactly the source's. Verified when generated: <c>opj_decompress</c>
    /// returns the source samples. They cover what J.11 does not: layers, RLCP, several code-blocks and precincts, tiles with image
    /// and tile offsets, the RCT, NL = 0, SOP and EPH, the reset, segmentation-symbol and predictable-termination code-block styles,
    /// 16-bit, signed 12-bit and 4-bit components, and a JP2 file.
    /// </summary>
    public static IReadOnlyList<JpxVector> Vectors { get; } =
    [
        new("Rlcp3Layers", 17, 13, 1, 8, false, "opj_compress -i Rlcp3Layers.pgm -o Rlcp3Layers.j2k -n 3 -p RLCP -r 20,8,1",
            "/0//UQApAAAAAAARAAAADQAAAAAAAAAAAAAAEQAAAA0AAAAAAAAAAAABBwEB/1IADAABAAMAAgQEAAH/XAAKQEBISFBISFD/ZAAlAAFDcmVhdGVkIGJ5IE9w" +
            "ZW5KUEVHIHZlcnNpb24gMi41LjT/kAAKAAAAAADJAAH/k8+YgBEdtlAQ/Wcob/EhTe0X8c6A/CIA/i3fqMEUCQAiRQLBbXL0UMlkJeBI/EG+MPA+cUBV/VMA" +
            "rX+Lon0qQuefFZePBUx2ts2Gx4CAwPkSQPkRQPnPAAyHFzwO95BdcbkvKmnp1R7rShwnmTcnyOcBhWTCU9AZlKEdmReHyE+bMvW4+DgQ5nem/0jnd5DTzINv" +
            "i2UOXG28KPXPzb8c0hYgAT6mj2SHPzIR4CtrdmyB9wvV7ywFZat3D8P/2Q=="),
        new("Rlcp1Layer", 17, 13, 1, 8, false, "opj_compress -i Rlcp1Layer.pgm -o Rlcp1Layer.j2k -n 3 -p RLCP",
            "/0//UQApAAAAAAARAAAADQAAAAAAAAAAAAAAEQAAAA0AAAAAAAAAAAABBwEB/1IADAABAAEAAgQEAAH/XAAKQEBISFBISFD/ZAAlAAFDcmVhdGVkIGJ5IE9w" +
            "ZW5KUEVHIHZlcnNpb24gMi41LjT/kAAKAAAAAAC+AAH/k8+0UBEdtlAQ/Wcob/EhTe0X8c7+Ld+owfONgfILgfOKIkXJZCXgSFX9UwCtfwLBbXKLon0qQuef" +
            "FZePBUx2ts2Gx8D5EkD5EUD5zwAMhxc8DveQXXG5Lypp6dUe60ocJ5k3J8jnAYVkwlPQGZShHZkXh8hPmzL1uPg4EOZ3pv9I53eQ08yDb4tlDlxtvCj1z82/" +
            "HNIWIAE+po9khz8yEeAra3ZsgfcL1e8sBWWrdw/D/9k="),
        new("Lrcp3LayersRct", 19, 11, 3, 8, false, "opj_compress -i Lrcp3LayersRct.ppm -o Lrcp3LayersRct.j2k -n 3 -r 30,10,1 -mct 1",
            "/0//UQAvAAAAAAATAAAACwAAAAAAAAAAAAAAEwAAAAsAAAAAAAAAAAADBwEBBwEBBwEB/1IADAAAAAMBAgQEAAH/XAAKQEBISFBISFD/ZAAlAAFDcmVhdGVk" +
            "IGJ5IE9wZW5KUEVHIHZlcnNpb24gMi41LjT/kAAKAAAAAAJCAAH/k8+UUAdLLJg//gq2I0vHCAI0g+fEYAjsboCAgICAgICA4QCLOaDhABMOQaqAgICAgPxB" +
            "gDrqtYEgv/zhwLx5di0vc4L84cB4S38vd+97wPkF/IHgPkFAEeJv7U9nn2nwZH+gMzqhLKbHFiBkKo2ken5bncHzjYPnGQfUGhxya4ZbBUm5Nbzee38LJklI" +
            "0VS2jieKLa8hpJE77L9ekJlWS4UOwfONg+cbB9QaHHJrhlruYUU4EBstfwt8yhjaoOB5fstMnechpJE77L9t9BRqEIWqwPkPQPkOQHyGgIPv8EQqaTh2P6AB" +
            "OQnUVFpo439jaSBBSFc3u1htbxlXhJ31pUIfTJIOFtGS62FgPkDZuJtvORVcjj8VKE6pv+aHAu3LzYSD18WPoZsBvsL5oMW0p8Hzq4PnUwfUTgyPqUxvxyNj" +
            "UAIbrSNM+mOotfxInThRANpSNDYm9fhsAsXrtPIOqqov+R8R1Rk9sSpCtWkZqoZAjNFxZ1W8ho6biF/0HW33bEk4BhJtcsHqVt08nzli5Kmj2GMWyTHQcP89" +
            "YM+vqrOdbrVDcgnI6cw7WQ1976IYx7Lbf8Hzq4PnTwfUTAyPqUxvxyNjUCkp02HrCU9jeexIyDcQX6ZEjQ25Sfp7j9pbEhEcFBWrs38WIE9+qPPhuOOqZy+h" +
            "df5hEBnFWblrlPLuJ3Dgo/LUK2mqYvPgk6c5YuSpo9hjFskx0HD/PWDPr6qyb2s1Q3IJxnFpjWQ134trQ/stt//Z"),
        new("PrecinctsAndSmallBlocks", 40, 36, 1, 8, false, "opj_compress -i PrecinctsAndSmallBlocks.pgm -o PrecinctsAndSmallBlocks.j2k -n 3 -b 8,4 -c [16,16],[8,8],[4,4]",
            "/0//UQApAAAAAAAoAAAAJAAAAAAAAAAAAAAAKAAAACQAAAAAAAAAAAABBwEB/1IADwEAAAEAAgEAAAEiM0T/XAAKQEBISFBISFD/ZAAlAAFDcmVhdGVkIGJ5" +
            "IE9wZW5KUEVHIHZlcnNpb24gMi41LjT/kAAKAAAAAAVJAAH/k8+0RBEdtnFHC3zV/2uJxXQTSv9zz7RAEg81A++ZYZyS77aQA+AVf8fUEATOnu+V6x+ox9Qe" +
            "EeWsmfLSLkXW10IQKHu/34CAI/mR/X+IbgL52JFClgOiF9+AUBlHuys+6DowZz/PtBQNBvpXP8+0FAH5bGBvz7QMCHavwfOLgfIMgfOMIkXQV5sDPgkgpFkU" +
            "KcEPRADrlKnElNoc/xJtyfoN0AE5kLnA+QVA+QVA+cYAFND6JabXx6SXYyPW+1a4TO8Den8P8sHe+SRZ5VDXaQXA+QNA+QNAfIHAF/nrXWErDJKJfqzrDBrx" +
            "f42rf8D5BUD5BcD5xgAJ36uNkqTsz2HzBzon9oCsDjj+O8ckMjFHXiG/ymJOx3TD6g6H1BkH1B4kaRMtbbwlsSGvO8OMhyRsBOIqZTuz/sTkfyQ5CAIwsWGX" +
            "bIUXsv6TH8faET8AiPwCgAQhGje3AcW/A+j5589Syk8YwPPhu9QnJjZfwHwhwPkBwHyBAAWaNQGquw0i5A/H2gs/AFj8AUAQlGfOPw3u3qhHDwjzbqXB84KP" +
            "tA4B8IQFMwRwXwG/4H5Cv5C+C+Qt+crgfnLfnKgMhxc8DvgkB9age22auugwvppcHosUNtodHQaJSlOySxGjndH0RYrTIgvNABeHyE+bMpbh2zJon9us9mY3" +
            "j19C9z8HZaK3QbTG7YAQbZORTVRX3+u3oa0c0hYf4q0u23Kd87y0jJjo15r+7FWVKytyGNdUR7VGBbe7eQ1L4qZQAiO/4H5C/0hPA/IV/IWwPzlr5CQUk6wE" +
            "CmhXOrQTS1ZZj48yW+pkm/ypkwx6sfYI6pVdlhOGFCL2R/F7cSkOL6HvlHPtNf9n5L9bEP30aUemVyUXhjdOYzxJif8KvX68GQqR3fuPAWS/FfPjMqoCLf78" +
            "Dwv0aLnCgc9aN7sfOwlqrG/q2No49ppHjK7Wds/BP+D842+QbgfkF/kFYH5xf5xII8D7ZR9N9ucLNJebvxF6FJMGRn0BsAxsW38BB8DfsXWDkEdlUQUOA++w" +
            "jY+/CakduXawn6ZovWysBRX9Cak12igFf+B+Qv9IUwPyFvyFcD85X85YDtEjxJclQf2fEpkx4VahhmJDsNez638Jyn0xA/Vzg4MjnGcscwXebPWlfxQwAJ2s" +
            "0DHmt/V/8q5wEblwc31sdy8eKpy7CDPj/pxeudvjxTzD7tGNG/4X3H58TxBjUXMU6AwRcj0k3HgGL0gbCaKraHXxeaD0gfAdrAQUzgbSkJ4/4T5C36hnEfIU" +
            "/aM4j5zH8BcNdrLjYK3SYHn8zLKSUuifw+IdyKrfM4QI4mDk0vDaiXC49EOjbv1GqwsM22IoHQ48awKRPPgqvk24/YHRoB5V9Nl7OVRQcEJtDgrSi4AIbcb+" +
            "AUuClLJWn6FzrxH4I2besU6sZhFIiveL06WpBNOB+t4GzjOAc1ptjDw7tEeSBFHLzjPuH0jcsm3D5Hzi/0BLIfIL/gJZB8gn+YoAI7TfeQ3diGHR/38HC6J8" +
            "0BPUUrE8+Mv+1wO9Jl8U/KHwv4YaxYsafwdm4z/JvdqiRY/Owlz7fHviNwrj/Uu6RrK4xxsYnqgEPNJIerw813hw2p2HVHC/wPkGwPkGQPnFgAwMCvjTlMCV" +
            "Yg2U2T8JwLL3p+GA5kISbX8hnRCKbVO3fF/QJs/AQn4CMfgIABkgHil4ydd0UGedJS3IpT8czl9sKTvOMtCn1u+UVn0lPxdg78mrtFMTAz//XpGIWrrD6giB" +
            "8gaB84cFGLwgOXqAjwS80EUUPw+xJ81yCX//2Q=="),
        new("Tiled", 21, 17, 3, 8, false, "opj_compress -i Tiled.ppm -o Tiled.j2k -n 2 -t 8,6 -mct 1 -d 3,5 -T 1,2",
            "/0//UQAvAAAAAAAYAAAAFgAAAAMAAAAFAAAACAAAAAYAAAABAAAAAgADBwEBBwEBBwEB/1IADAAAAAEBAQQEAAH/XAAHQEBISFD/ZAAlAAFDcmVhdGVkIGJ5" +
            "IE9wZW5KUEVHIHZlcnNpb24gMi41LjT/kAAKAAAAAABoAAH/k8+0EAYUkt/H1AYCnazH1AYJPRXAfCHA+QLAfIFABXx/B8gY+L8EgQcnL8D5AcHzhoPqBgFk" +
            "Xw/WERT8PxFlSd1aj8D5AcHzhoPqBgsa3w/RnQoCTxFjBUN93/+QAAoAAQAAAHQAAf+Tx9QICPxq7cfUCgIGOoY/x9QICTBcT8D5AcD5A8B8gYAG1vcGQBg+" +
            "u9+/ETUpkMY/wfOEg+cPA+cOBBodqQy54yZSzG0MB+hs1HhSwfOEg+cPA+cOBBodqQy54yZSzG0MB+hs1HhS/5AACgACAAAAagAB/5PD5wYIsknH1AYCcGLH" +
            "1AgIbvt/wPkBwPkCwD4RQAbl6wZLFkIYCBOj/PXB84SD5w0D5wwDeUH+EF/fK/vfBu3oN10fwfOEg+cNA+cMA3lB/hBf3yv73wbt6DddH/+QAAoAAwAAAJEA" +
            "Af+Tx9QSCOtDSOyaK3kPx9QSCr0VecOEZePfx9QSCC7Vhy56q7e/wHwjQPkDQD4RQAhIPKOn7w0VG0lIRgWIyyOnwfOJg+cRA+cQDkF/salMCOVzEdiLuNKp" +
            "ekcLKxEPB80dMcHziYPnEQPnEA5Bf7GpTAjlcxHYi7jSqXpHCysRDwfNHTH/kAAKAAQAAACnAAH/k8fUFgg6A7PG2DTDtCp/x9QWAjMkGn3fHHNq7+vH1BYJ" +
            "EL8zzN9Ooau/r8B8I8D5BEA+EYAKiQcPW1TIEDKa5nIBJD8Mxqg53zfB84qD5xcH1BQHZ5uatsJ9SuWfDJkGoGMd+MO3Wn8MU/74+iaimlIfwfOKg+cXB9QU" +
            "B2ebmrbCfUrlnwyZBqBjHfjDt1p/DFP++PomoppSH/+QAAoABQAAAJkAAf+Tw+cOBp9Zte1dEMfUEAJiPk9X4Q3fx9QOCOvR9JoBu8D5BMD5A0A+EcAMM793" +
            "H4FqLI8MvGzAGs8NKNzbqeIfwfOLg+cRB9QUDGqLSONkR0tX+l8apjDhmRvd3wRzZYB/1cZI3aXB84uD5xEH1BQMaotI42RHS1f6XxqmMOGZG93fBHNlgH/V" +
            "xkjdpf+QAAoABgAAAJIAAf+Tw+cQCDqfmEUDJe/H1BAKroY3JzOXw8fUEgjqPfNCWX4Af8D5A8D5A8A+EYAG340EjlMvCt/L7/iWvQvJDYS8x8HziIPnEwfU" +
            "EAuCDwuwJlKTDGnFwIXDA0gfGvy9iSvWKNXB84iD5xMH1BALgg8LsCZSkwxpxcCFwwNIHxr8vYkr1ijV/5AACgAHAAAApQAB/5PD5xITB8nqjVwT9V/H1BYC" +
            "4oz0Y1p5OljIy8fUGAjnqHO9Y1kve/f9f8D5BED5BEB8ggAG2w+b7f2gPxMnzPWaMjyfGyeyMqRyf1TB84uD5xMD5xIQS+KI4owVd7Z7zyEVD5SpLGXa2QLc" +
            "oQ+d4Ef8q8Hzi4PnEwPnEhBL4ojijBV3tnvPIRUPlKksZdrZAtyhD53gR/yr/5AACgAIAAAAlQAB/5PH1BIMov5eRTWM/3/H1BILM9iFbTrdMj/H1BIJELmB" +
            "ciEAQ3/AfCPAfCNAPhGACDD0uxFXcwxqcRd0fwWLbC0s18HzioPnDwfUEgueWh3CKPMPKD8VlA0HZ366HSDdkfj5uWWXwfOKg+cPB9QSC55aHcIo8w8oPxWU" +
            "DQdnfrodIN2R+Pm5ZZf/kAAKAAkAAABRAAH/k8HyAwrGL8fUCALn83/H1AYJDWXAOgwHwhQD4QgIAbYB18D5AUHzg4HzgwWoBXexAxdrwPkBQfODgfODBagF" +
            "d7EDF2v/kAAKAAoAAABlAAH/k8fUCA0HRX/H1AoCBjzhP8fUCAkwVSHAfCHAOhQD4QwGR48KvwIjFcHzhYPnCwfUCg5bHlJ/Dlqc708MBkH6n8HzhYPnCwfU" +
            "Cg5bHlJ/Dlqc708MBkH6n/+QAAoACwAAAFcAAf+Tx9QIAkrif8fUBgKVs8fUBgk4R8A6FA+QHAPhEAmRAW9fCWjiH8Hzg4PnBwPnBhAjHwZuhw4mf8Hzg4Pn" +
            "BwPnBhAjHwZuhw4mf//Z"),
        new("NoDecomposition", 9, 5, 1, 8, false, "opj_compress -i NoDecomposition.pgm -o NoDecomposition.j2k -n 1",
            "/0//UQApAAAAAAAJAAAABQAAAAAAAAAAAAAACQAAAAUAAAAAAAAAAAABBwEB/1IADAAAAAEAAAQEAAH/XAAEQED/ZAAlAAFDcmVhdGVkIGJ5IE9wZW5KUEVH" +
            "IHZlcnNpb24gMi41LjT/kAAKAAAAAAA5AAH/k9+BQBIlvYx0fPNQibqmPlWEE9f4zDhW/Wn8GSL77T2JHb0L8Rmv8HICYk//2Q=="),
        new("SopEph", 13, 9, 3, 8, false, "opj_compress -i SopEph.ppm -o SopEph.j2k -n 2 -SOP -EPH -mct 0",
            "/0//UQAvAAAAAAANAAAACQAAAAAAAAAAAAAADQAAAAkAAAAAAAAAAAADBwEBBwEBBwEB/1IADAYAAAEAAQQEAAH/XAAHQEBISFD/ZAAlAAFDcmVhdGVkIGJ5" +
            "IE9wZW5KUEVHIHZlcnNpb24gMi41LjT/kAAKAAAAAAFvAAH/k/+RAAQAAN+BCP+SEh5FYX8D8iStHoVceYvFqpI4b0MBwdm+dQAlC7WS2PC//5EABAABz7R8" +
            "/5IQ4OHiq7gxeRbOKKIqrkHWk9JNKaVw9E/GUt1UpGFj/5EABAACx9Q4/5IQ4VEmOmZuLPl6URNkmEytcw2umazl1JxJ7Nqn/5EABAADwPkKwPkJQPnJgP+S" +
            "DIcXO/HCoevSAHbGxmnbUWem/l+/F4fIT5su3KY3JiGN9MaJIxOEHNIWNUl4m/l5PFeo8KORumAuP/+RAAQABMD5C0Hzk4Hzkv+SEBS6pQtea1HLaTeW80yO" +
            "tnLUIed+fydoxHGoCcq9bJPDtIq++16Ldz0HHluk9U7tsgqBQi/yIPxNbEv/kQAEAAXA+QrA+QlA+cmA/5IMhxc78bVhcl47iOAvzCsXkU+CJt8SmWBdY7wI" +
            "SptX4+DyO3BWWrUc0hY1SXibM+nB7K6HBpDDTAXH/9k="),
        new("ResetSegmarkPterm", 23, 10, 1, 8, false, "opj_compress -i ResetSegmarkPterm.pgm -o ResetSegmarkPterm.j2k -n 2 -M 50",
            "/0//UQApAAAAAAAXAAAACgAAAAAAAAAAAAAAFwAAAAoAAAAAAAAAAAABBwEB/1IADAAAAAEAAQQEMgH/XAAHQEBISFD/ZAAlAAFDcmVhdGVkIGJ5IE9wZW5K" +
            "UEVHIHZlcnNpb24gMi41LjT/kAAKAAAAAADGAAH/k9+ByBIssfGv1GR79zXsYbyStbhNbaa6euNQQnHzaQgmoacgvs04YWXvag/bRiIGU198AZ4o+e1bHbb3" +
            "N8D5EsHzqYHzpQyHFzwO97SVs0uKoMeYyXSlr2ko4uUysXZSeSmWkL34hx5WPAl+Xzco61BPmu/MiEFmL+QM4plpyLu0DMT74fXds/YVZrgemwo+NOmyEBzS" +
            "Fh/wwE/Q2wJj9JDK8hyxtVJXv0GoA1dL9ueuQfXvqeFxbB7/2Q=="),
        new("Gray16", 9, 7, 1, 16, false, "opj_compress -i Gray16.pgm -o Gray16.j2k -n 2",
            "/0//UQApAAAAAAAJAAAABwAAAAAAAAAAAAAACQAAAAcAAAAAAAAAAAABDwEB/1IADAAAAAEAAQQEAAH/XAAHQICIiJD/ZAAlAAFDcmVhdGVkIGJ5IE9wZW5K" +
            "UEVHIHZlcnNpb24gMi41LjT/kAAKAAAAAACMAAH/k9/4kUASEVPCJXhsCaLPtDjVi52L54MX1cLBPS4Dm7dWl8N5e1rm7/Gemx7+wP8BuB/gZgf+ACYMjLmN" +
            "4E8eEpiUAMwHFpJeGXojs2goLxkQN2sOht8HyE9L1tg073L9ECrZODXlkWZYBP8fFUvWfO2ZC3XO7uaMPMQXPxPb5P/Z"),
        new("Signed12", 11, 6, 1, 12, true, "opj_compress -i Signed12.raw -o Signed12.j2k -n 2 -F 11,6,1,12,s",
            "/0//UQApAAAAAAALAAAABgAAAAAAAAAAAAAACwAAAAYAAAAAAAAAAAABiwEB/1IADAAAAAEAAQQEAAH/XAAHQGBoaHD/ZAAlAAFDcmVhdGVkIGJ5IE9wZW5K" +
            "UEVHIHZlcnNpb24gMi41LjT/kAAKAAAAAACKAAH/k8/keAhhphSm3iEAFfh5XVwF2/B+IdbfOlMuJIzxTln2gcfyMz/A/H+BoAiJlRi4LgwAtAnkplUznl1E" +
            "zzLSZqcZbscbQPM2BDev0BTkSCL5KVbA1xo3ZMvTZrh9i63HfWBnFf9gkHc3adv/GsH1xUe3olu0dQU+h2A+0p//2Q=="),
        new("Gray4", 10, 3, 1, 4, false, "opj_compress -i Gray4.raw -o Gray4.j2k -n 1 -F 10,3,1,4,u",
            "/0//UQApAAAAAAAKAAAAAwAAAAAAAAAAAAAACgAAAAMAAAAAAAAAAAABAwEB/1IADAAAAAEAAAQEAAH/XAAEQCD/ZAAlAAFDcmVhdGVkIGJ5IE9wZW5KUEVH" +
            "IHZlcnNpb24gMi41LjT/kAAKAAAAAAAkAAH/k98hMAaC9DvHJb4gaTuoalfGIPC/ORz/2Q=="),
        new("Jp2Rgb", 12, 8, 3, 8, false, "opj_compress -i Jp2Rgb.ppm -o Jp2Rgb.jp2 -n 2",
            "AAAADGpQICANCocKAAAAFGZ0eXBqcDIgAAAAAGpwMiAAAAAtanAyaAAAABZpaGRyAAAACAAAAAwAAwcHAAAAAAAPY29scgEAAAAAABAAAAGRanAyY/9P/1EA" +
            "LwAAAAAADAAAAAgAAAAAAAAAAAAAAAwAAAAIAAAAAAAAAAAAAwcBAQcBAQcBAf9SAAwAAAABAQEEBAAB/1wAB0BASEhQ/2QAJQABQ3JlYXRlZCBieSBPcGVu" +
            "SlBFRyB2ZXJzaW9uIDIuNS40/5AACgAAAAABFgAB/5PPtFgQ4lyWmkeKunn+t4M8v2NyF1oyd1xfx9QqE314wtMwvMrRWAiTvlTDPEiX5PN/x9QqER4pOCdW" +
            "4NFSSiw+TvZwTa23pNY/wPkGwPkIQHyDwC4BKnUXLsBXjAOH1r0ZGUM3wNEHznbuJB3/Ynh/FShUmufsAYVcA/3ZNvNZwfOTg+cpB9QsDI+nbCESYwr0aPyp" +
            "BB60LpLtfxGMHdofkRw6/0/Icc1Nm7EypZ0BH5ZZ1yU/mDaV9A1LWhdEqbhlBJsyF8HzkoPnJwfUKgyPp5lTuL4qws11YDGcRdjPOwAhF4vtDtzXm0qzk3KJ" +
            "SPFIj8cfllnXJT+YNpX0DXVvFITFKdNPWEf/2Q=="),
        new("Rpcl", 8, 8, 1, 8, false, "opj_compress -i Rpcl.pgm -o Rpcl.j2k -n 2 -p RPCL",
            "/0//UQApAAAAAAAIAAAACAAAAAAAAAAAAAAACAAAAAgAAAAAAAAAAAABBwEB/1IADAACAAEAAQQEAAH/XAAHQEBISFD/ZAAlAAFDcmVhdGVkIGJ5IE9wZW5K" +
            "UEVHIHZlcnNpb24gMi41LjT/kAAKAAAAAABNAAH/k9+AgBIL01zlamtN4dhJW2VPrL3A+QXA+QXA+cYADIdAtfaQ/dBUkCcXhiV+CBQ7Pgu7AxzSFYWcrTVg" +
            "8DoCWv/Z"),
        new("Medium", 64, 48, 3, 8, false, "opj_compress -i Medium.ppm -o Medium.j2k -n 4 -r 40,10,1",
            "/0//UQAvAAAAAABAAAAAMAAAAAAAAAAAAAAAQAAAADAAAAAAAAAAAAADBwEBBwEBBwEB/1IADAAAAAMBAwQEAAH/XAANQEBISFBISFBISFD/ZAAlAAFDcmVh" +
            "dGVkIGJ5IE9wZW5KUEVHIHZlcnNpb24gMi41LjT/kAAKAAAAAB5xAAH/k8+BQBHlrNH6b45wm9b/Vfztcs4NhB1W/lLgL5zTmE9fwY+mShSrPEmqWfGWt+eN" +
            "2zv+QsAy+dI2r2nyc7lhusax7dEph3tEfIl/gM4OcUA1bVYfixrYuMYlsPIppkgt1sXR/rMKESOAgICAgIDxQP9Kgcv/CZWGxtTIzw63bvRQImmqjCfDhYcX" +
            "DiQkCjR/MB69XdRtGYgkf+HBHuUl/EO11iuP+M4WOlDW8iYjjxXilzPE7K6Q1O21BSwUO/bve+2r8mvHF/ARGSAkizu9ocdx8e5WY64F6q5J5EI+H6IaD/gU" +
            "vOPtx0+OoQnAgy0iane1NwcZ0+Ak5T2qUcZGUMHiBpTohZOLAyP0qlVolRhIZtvOozqc6WBsgoM1NjLSF1IZV0EUv2qe0VEVftwMPUPD0ZaDEslQlVj2W9dV" +
            "vI23+Z5lelinnM6lOqRWAIM7wjS3ZFnYx3sOwLwBqL9zvFEaQTHfryOP1C8RYH2yxJ/pMt5SVvX0lkqRzUP/KUjMxacbRR2riejvlqt8BOHEOInxrergKb9s" +
            "2Yhc4VudR1nG6isN5ivVwCcqTi/LKA/nnJBtnQirMgpp8d++rBHnpAC09jrf8z+p+DEMwLNpg7v2TWU4vS5kzO9D+/b7AYJDCHHlcb9/zm5m2k7WgIHpwmxn" +
            "YBG/nmu9L86FSuAlJP6leCNrnwbKHjAAS02C0lCUfnDRiSppqFsiL9WLj52ksh7k4UokNoRogLMNiTb7w5yPHMqkzJJlpJBCbX76QH++C7OfZLe2AFW+hT31" +
            "E3I7wDdmGbqvWVG9/G62FMvtUMt3jaDC2Enm/yByLtcLfewVm0ZztTlLPJj6QBA5Bu+5mIjx37cv0LnLevWs7ZiJve8Wq1TszuXgE+7bS8YmLEkT2LzSpTz4" +
            "VcXlPjXBjRdqAbAKVHdE8kxfHfc+fgdjcSONmusd8n52pfigueuVC7Gweeb4ckIR2naCcuTgdxV+aqvNc2n83j9SPh8Gsdu6O8Grh9RX8tve6AfibOU6IYs+" +
            "X02c5IvqnZFENFlzRCzJzj4+sfyEgNMaIQKT4H7pxvkr7fCqpj63OPzmQLLfDP7olcObqNWudFsr7qAnFly0bXmnFlf8pgCNE3Omy7uWbfHal/OE7oX9QPL4" +
            "OKsRWF385/5zf1D42Ze8B++KifFaukMHXJNqFHv4u2vOnvqefUYNLu6Xu21oQFuDcr/aPoYB0DnlTckeLxbRmo4EEJkTGacPZRGcMxnnXTZDNQSRhdS+T7vZ" +
            "QTNJLNKEbksV9Bv9B36h37CYby9tK30KKNOy9q+kbriEJ50QM3+MFRTrCJFDyIO1+HcM2orqK0dLE5AMHQow6KvEOMg7XPOnXzHq19LV4cuBOrMzitJeIMCp" +
            "0BqRCBkRUMdKjUfy5Au3cKTBg84tTBN//UQ/Qb+4nE9WvBK+oONkHvMt+5Os8DwVjviCFCy8qasvvJ30OZD3L2CHwgypJpa/wC88TmXSBE/F3TsEaujW6594" +
            "b1/ZfD8hUV0ADR39PxZmALZDeDu3jr0GVbYFJU2g6g8V81qbL6MqB/03n6b79lIgSVUG0XJPBOCl1uNKsn+vHhu8AyWZO+WmgSRvnC6jMpu0o1iVTqtoxRw6" +
            "bN9A/XUvB/EWIxXV6SoqYRJebWRD08LbRnCA3R7uEDeAzk2MnsxxyZZ+40dgGOHlv20biZ5oVwZctK1Q03fCu2CEBGMWeOlgFeBr28Ih18vF9ThI7IxlCbGB" +
            "VodbQRaBan/hbXC33L7mJG1qP20ZYNSLQsVhRKqEUYbiCqtCXqkaRzmICdoXOsT2ISv+nH2uj1FY62em7p8qQzNC4JCIe5X6jfw1q811VogZYeSJ5Ad82WtC" +
            "hS0Ufg6EvkyhTnjCHPIaBP0lsLmW7EzzXdEveFmuw5z28ZhNeiU5hWIfcNgh4EkjLAoDtGsR7HlfvNZyjfslweMZpQ6/G07R1QG75bwLWt0mf1Y6cacFc3a9" +
            "C/sePmiNkwJa5GuBBUiNF1gpdz432qVwasQelT3qPKtzTRtTS9ee4NZJsRhklznvvKEUrCnb+vof2yqNUzKVKarF6RgNDEEBboJud51Lf/2Uj9lL/fVwU008" +
            "sdkQ6zU+5CRcqsetLJG8DSCPdXyptRdqCcWs5OILnHUgQMEvFgWcSD/LUT6y/xA8jTcXPs+BkLe2HDV0Zq0YQIxuhNkiVaFyV2iOIkO4ZSlXePfX/clHa8PK" +
            "o4PMzp44uTtn6JAdWB8mP0LSxJOZKzNaq1p0vZu0tTjfZn4s+eDu9KDFt5uLiZ6dR0bRv0zKgcPfPOcw4JJkpdumqRksi2b9NSFkz5w/u3jFil9VhX2CO63j" +
            "UjkYrKWolR2M3TRrNfgmjeQLeneDD57v3BIdLcrlSuxz40vKkqWIjvK5krrQcj6h1kAMNE5rWdfh8yo+2OqH7yOv8jFV7sKTftmF0Av6teXHDZ3xnbFwH0Kr" +
            "zvaQpT9JTd5biMjKL7QGYDcqUW3okj/yz/D4ymk/7iHItXFHrzOv7ru409L/Vp9jzsz4UtlerNLlnYUlKHDS1u+SqkqfBktCf4zCnZSuB0ma20XpKYSCMOv5" +
            "s2Fv16RxhX3dr99SwiDILE8PN8erQ/ew2HWt1hVgA00gX7nd52MeExFiq3MpsqZ/nhpNyEIeVoxCASoY6yK8zw8cbDcMUUxGk4trnwhDy7Ve2nLaUqbSV+X6" +
            "IjzWdYS/+/oPxiVAN5eeCzv9lJfZSn3q4I8r+mK1BBbapZ+yAiR4Gz3tRffrT0n4I43ihqo36OYPDW7OsOQsB1kpVM4r4ltLLcNBP1FtcVybUaZhqX0csZrm" +
            "xJZRyIYFFMYUs6K2omZ4cFLOrSgsPDIEtyZuX7lorc6AIBOm1ymFZuzuFOhEzxVEWbV7A/XbkLBwQofmrGfYUu/vX62kfh33rWgsb7Twsixxn4lHcZ57K9P8" +
            "I9ggB1mZ5D3e9wnA60i1SAqsLBfdeTzPqTUOEXodyxIZNpi0DIBnl+ptpXPN39Aw+tRrm34PAte8NTnrr65nyJ2xpSTx8VJjwLdW4YaQyp0Y13diEn1KplyR" +
            "ejlyOL/WAWxR/nb5vnvp/i+hMXnzbGJwbdgXlybTiKFyEpb346TuKy9sQvykzhjXf9GD8xk4nsM+5G+lUPtCen2LBN9DgW4PByRKqovNtjBZE8npIZI2uc4F" +
            "yUql/bac82uO6nJg6C83/38vydYu2BFAySVruy51z5Tu3m0gFN19jP0DSuXSkW2o2UGpjfYhunqVLRAlX4ZY/xnQMVmpGMuT1tGlpofmHt4iI3vEciOQi1P6" +
            "OdWsYdw/VvMzCfETNM1L5Sx5Bp8cZzehsqSvfvpBBbrh1ad+qGAEHuJZe/2P99b5fZfAfbLNLTZPx2KewBsQjC0yYwdtliXVJSH6/lbOEk1wpt4esemViPr0" +
            "JFXFm+WaYEGL7c5qezzMg3JvisgDQdtfMCiKKbZRnbJLt5PCyx5Ruj5cUx95CUXKIl+GNSiNbsUsgLNdQQuT6i1F6HWl+2iHgSbI/SlYGjcjiJXTXTMyrSwT" +
            "SzQBELPdFk7FiVGXsspdjFI96TgHmn2JTY8FUUg9ppvI0khBZX6b5ebDnduvTFGkwCE8E8pxJHrhKcSW62HG1npppt+4IsM9ergToTW/Jl0y7+QddWeHEv5M" +
            "o8gNalEF26z91DH5m2Ptkv96wTBrkZzxV7uZMag16BRk8nuJeW+N0/16HK6U9QfxGvqPrfMY+HVhfLfDLJTjyK7FojhffYxQrQKefJzCMAMBfqufe4IIDWC+" +
            "sdwXV2TTMamxJCWZJayLb3rnmlsn0k+HlW++e9i9kZAUAzAQRPksnyjBX/DYJtG3H04/iLcqopt3ofTBs7CLW5DQFjQWNPDMUMIUU8a4PXPJ960Gl5f9xoRC" +
            "zOHoPUhDXJCrEzeWOUsA13DH32wpbZNkrVpUD7MyHoV3BPZTxd9n12f9nrx+P0gI7GEHerUOemKpGZvI0kQXEU6mcsbk+Or7TR0ox7H51WYa6wB6KZmuk+EN" +
            "IcyYVrY6kociy9NdwJEFGqYnaWSfnmt3AhwezMuspzPzJwYvuay452CkuYEKFg8suT33/rtBru158IpPNxSzq8mTLBqfG/LJbTvAvLXthCfqnMc/alY7mA3v" +
            "DX+Bu5doFrwDoElVExkl1jNB9ZnYW+bl2e2u3JG1WD19ChjRzzmaxSFqGua9vBBFqfhAkmvY/wZij1PmswNKwjnMe9YzA2kkFBoqI2raqqMvVwGwLlfU5PZ1" +
            "7mu9XGQCLKHO4kwnzaRrXRUOGcBe037+UxAnj+4IDoi5bnYYERclzNDRIv5/ku8VKypn27OzenhUYYg8ae9NOvOEjRDsJvEi0FzC2JJYI8D8xYcmtApQTYrz" +
            "MRPwDQx70aHI2FVcyO5GIBsC5cnTEOuQEQUII02Fog6tSPo3dtDDRxuuPy7GyXdRpp4lB/NmLUayhG4HJvBhLpPr/4Emmdowy9QMZxnJ8vBJEqsziPvuh/3d" +
            "IXs6rGXQaqyL6UJ4Wm03+WLwrdvkXWiXOAk09NGpMGIjZ/G8jrDJvUVIK87YXh6jq8WUYKHn8vLRSTtKW/ESS5LPH5wCQwV94ueu4umGaR2X21/2BvcU7eR7" +
            "ObepfXubuny7DdojfXhRPBsKxywh4K2zDY4Yw8aa61vDn2Ki0OQLp1eKRydH5PUshJ6s/yoAE3OXCXyii67qZ5gU1Q23grT39YVWXD+IeT4cGZou9FkHCtP9" +
            "JwLLFnj3CkXPPYTb4AzRjakV3PY0wWKsoOQqJOrVePCsB5Ff6x5ZjUAlUsqQlFi/OyoIzg8khpoeg3o8BpRhoQ7Tyu/BUQDG61DOwo7NdTiWW00O+ZboDKtC" +
            "g/Ozp8t/ZQ6/zJt6IsWgHzSo7J6n3tDmpFSe642Wm7vP/h4V6+pfQIlKExORgFMiTa8cynuWcJNkI8C3EiC08Ul9RspWN1WbVUIw1Sq4L6OZ22g9TCUaWUg/" +
            "psj+xRy9N4seHZuB9WK0JzrevVHTEWXhzIs1oP7unmaeSXifCVhPJ7YvWV4TW7dgEi1xuGvB6X37SVz7h6xeRg1ejr9S/AiFg4iT4IJbseatnhw2iwDzdK8w" +
            "f0A0iLVs7MbFctGzjMuDwMiEbD6tJySjiafkAUl9SQnmD0sfuTtvECjETOt5My+81S+a5HXACHLgvCccFOysodL0q4KqEKPGJSxXoxf0BYmoiO+JpZbX68Yo" +
            "mUotU/TIw29E9XHLfS1Q+g8AYoXcA06L8Y+dz4agIO/zW7TJXmzWiElp4qPBPcPLs2w3T5exZjuZDmrtk/rHszgbftn6/n1+ariUDZefgWE2XvBd2S9Aw5Yv" +
            "u2TbG+AGCJRkaY6aghCN7jt9DDKVd9ulp/20sfdS39+oEJ7h5WO4qVqQ0/XOI/wXNn8fieyFobRqxcLYOAd0qr+Ve5m1OWODrASlIjypTKj+bD8IVXsyjKp4" +
            "UJHxm/4rWPd2egjd9cC43UemgszYm9lEyQr3P9v2spxtogm3WnOaoktPNVOGczJnT1FUTd+egCOf4BCM07BIQGvkm0Z4vvglDmwv5uJYjLW9B8hrT/2joUl6" +
            "/ONvubkHcUYCDlfFZum4+7ut5WOC5cyRjMmjYJUGAFmM5E74GpDpeS77Sf9Cd9hcNHiPdVmQO4UozQZtNID67nctI39ym9tFLx6MlUsn07NQVrymXlOw2E76" +
            "AwwdTKNRxQf3qvjA29s08ObZeO33EH6zf4pHB0liIbwbXG7GvfIFwLdembwo01HNj7MpxAXjcNx+k66dA0eGnevWHQP3umCGVJw8anNTHwdNcASH7i119/wn" +
            "FMX8SarZE/cnMJ58UrVXWUwsRQFoeN3Y3dukG0mMuLTds9rUghIV10p5nJgR9n0/S+V+b8Z7feQupkeGKekAJP9uGSu8FS08pg3TwCqL3qWuyBlbpCT/K+h9" +
            "1nmOq6TqlfXIEakNq9WZmsqedmVJsCgjeci5N94kjW1Qmwxbjhza84oibKE9li/A1SmnWZLMCgblQjmHnE8qgFRD7/SwH0uuJUYKvgGySg1RVjjk2rFjjyVB" +
            "KjsoQ+aQEHwZoT7FyKmX7njMUOyT6Hc7K47sS/O0j77kcWeJXm3zLMCea3CXEnBMHOYaw7hXCvocS0udWMbZi3LX/TJu7uLfxR+pEYOYlHf5tKqEaZYGZQuv" +
            "YLr8Lx5yKZQ6H4MQMXYdng46JYhMZ95e0/w0f8qnv0siqnLqJdOlSzQCOyTJinGUBnusuTvVRyT/KXmVsk0f+08s3f7rrEo0ZjG1ATrVxT4gsF5XE/b0zLkD" +
            "QE53Vs18R82xowUG9OS7TRX3Cn31QJ5PpnbujiV/VSMwlEiGDdF0rUu44X2QbFMXKfQYIkwU33seCKeZ1Hh8jojyogH7albEGYB/3vwRE+VmobnGTou0Y/JM" +
            "kyi+4TjaPTaAtw8e0Ywp74bsOvaQNaUx7QHolkrAtnNk1l8UB+mORcSlJAF7Y7FHUFDxxagqIDnpJe/PD939y8K4oucCzcaLWhfC23AiKOoRaMhNpvMsi5Pw" +
            "VNAOcJxUMOKNDMZHO5bnz7TnLklUihaFTR2xDa0apcvRNCTmYkYkC5W6earJJQqw3zgcRmbd9R5DAek/buXpM9vtakoB+mo93bTIuopICYi6UqFGVsBHy5P8" +
            "cIeafrvvpoQO2FLiZ62r41AWHk1JdPSFUXC6skaYkq1upsgr/Fb+prFA++3z0w4eY89QRbrJIqMiF+p2srNXuXbltz8YIbM8FlvzURtRftZLHJx1G/AcRjOd" +
            "MW8nx7a/WPhLW3le/Iptg9vaVoPI47bgN5A+mOhBbylcR4NA4MHGbUsqQjilZuMIlP5cPy08MJe1cwBiiPj3Yfn8IACWVA7t45Qk+aQBi03GxYD13geoxTF+" +
            "nq5WKeePQKCNAL9YOJau4RZRSsQHCCOck+Ai6PVr+mfP6nxEgrhAnAFGTwZDUMZUFMarMsJhqQgZqbbX8mz7HqMYDxwvuJKD3lbyNHYv1lAgk15AvPJ72yvA" +
            "YHqnKfXuaoahYvQmo3bKZIwKfiWTsToaFBEFVbvxAQUZtuT4/Oo3dPMFf+5UOwPljSdm0UuwH4l2ym56qE6FED+xcXLP31FVQ+LRST4NIaovx+5ze0FALYRz" +
            "RNsiNGCi7WSI23ZwfIPPuaUr+SxBfgntCF/V533H11xmgLLbTIOmwnnGvAERBarEYPc2Lm41QpNUrUGBRe0tpffiyAUGC0IPRza/vUWYGUzcE1U7aw7DkWKV" +
            "1Ctf7M4u3WMMOth+Jj4gqhhWUf8Bl9Lxvt37P5RnLNVYWoxSrBpv3fZn2NpG9tCIlj6/qQXR+Daer9kiUaJjqEponMIryEkVrfabtAi1DViSV6ZBkXjl4CBa" +
            "KsCnIAAe1AnYziEssg8phO3iYzajrLa49xNIneatyw8YMtwhlQ9A8VNi+GkMroAdFJaHjNin0BmMwAwiQ9VmNAw0S1ZyRmzMsm5m2try3FUcgUPHt/Yn4eSC" +
            "XY2Xe7PqcZkMmuCOtLC7Is5I+TqhBbC9jWBJkK32PDZH17OAnT+r3tD5VFm26gt8/w0VORjG+IQWSs9+cw1NdyUv9OGcCFsh/rru2RE7wBZ0X2IJvmRfzhFN" +
            "QAzxQ3QFrz0qQvRf+Cv2Kv9GgkyiUy0ykmXgg8O+YTd5BzBR5ZWWFTTZknvJy32Xrg0bHBjko41pPqIeIv87JX4lsAXEAqy7Jp2OVLtrFLnM+zjuRRZun7Ai" +
            "yrJw/GqndPydlGXNXCYkSl8Jr/Wez/5OToWgxQ2WMwZWPwos7Ig7SXUNX93BgxoCQPzufs+pkPXKMhCSO44v0IkpGE6SCx83iNS/fpV4/fTx92mH79QgGB/D" +
            "qsMBvGZqb7kJ6074AwLX/BNgYL8l+UKYaP9bG03BzLNjaZkisxVSxVZ5NfASte3SyFQ6wleD55nasuUlX3HBh5DrSPTyQFV6sJQA/lQZcpYBMk6bWQ+GgDNs" +
            "HWxcogTnWgtCNnu9/3etzvSwrUcbM8+ApGxcrf3VojBX1M69B4xx9Rd3RGBXFN0TeB1B4ON4291T+O0YL9gezeJmwSQlkepG4ww6w4z17hGjhPfz9ULIW9cd" +
            "RIFdqumOMgKoCTdD7rqxvgprj4MnY6WZ1fBklB/ge5HY8/uNjKx/pRRAGR3PMdTNLl0mQ+F2vFcpIRlnOdlRWG5k6XG/EfUuBu9zmGCF4F205CaDDGgcr82s" +
            "9e73KTLq4vy/EyKuZ19K9X+QCEzYDleNJz0woYLAIkQjbOE27uftDpqaa4p6irec2DAJI7Ad9ktaByeBbqMGZ+SQgfaZHK0fyVA913HulH4l30dFUa5f6n2q" +
            "To0Y5d60FzDylXGfMzVoMlaK/Lg2mWFXHRM/tHfwEBNosK0cTvMic114u+eLzH9Av9yg7vdMW0OFuSjgStBIaQSU5Se4ZrwplfIDu9ZHFc23C6FDvjvZvdRI" +
            "zcAeVvBBVZrKnaz6XaVDlBjdnZutQRnHmaf6GX0MHdtkG9D8fhIL7rv3mGVvOVRkHdQCLlgvOiG8UVg/4THCOY4Wqa55Ub0nHWRzlDyxTjVPVjliVDMALdUe" +
            "HcKEr+AHX1WIaizeUh6Soxr+547Qxni1kdgQR3mbH3CksBNRr+hbDssQuTnqACY0cEN9fbx8DIkf0mmK/b49rPun33f2JXV0ycxrfcfSziJ6b2D7AkjCrH/W" +
            "7nX6C4c/dJgy0W4DIS2ITwZI++VGuwbeRrNaqvodTZfPzbmiwPs+z+bLPfW3zg/75M95Ej5FqfAfpoD0eotMFgU460P9pAepohcFcHEZfbY3izAi/kOPfXhs" +
            "3xV9Pc74wJ/we+DPF149n+i10mGe5Rbd5VWlUrCnbbxvv0m7rGiP7jNHHum6cZBLUfMnUxtapBiXZJDEsHIxwHBqvWwGWSs5R291XKwnvV0iH30k6s/k/kjh" +
            "h5jaw+JzlMH9927XEcl3P3Kx1P1XQDQNIE3Ky/XqdsMSV/IfPcTnEXDOYW1NCWlY3N9qrfoSQ26glUtoCnTrph59BxVIhPOFTqm382B2/jCwvl5dSsY0UbCb" +
            "J5ZOrTisauoaT5Dwn1dqxuWd3islRBeU/whcMw5OsR87ySE87MGtbUraOStgVaO7xJOkk4Ac7YgmmvWo4zcA+QA85d7SHypEfGpDTozmPSLrbx0jTr6TeKja" +
            "Ouxr8N/1AaKawd+ncWzed3Ilb0Koc1w1gZe1OO7VpzumPXXJJgzRga692P7ONPpgB9KpIRIBRRqkVWWG7e3BNwsq0a7OZJdJxR5I3RKXYXs6fJUrGWqcVoSq" +
            "PL0Dbe09UIOFYbVuJ7E2qo+Nj7kX+HYwWnH1yQpXAM5LQk7KoZyqBWwN9B5IMV4wB1LVD59t9YccsLhdovgOolgtJUt+nyv/Xbv8Uzft1TwlPsWA9d4HhssB" +
            "Q9SHqeL2k7yEd7ZCwan21xNILPiA2sm6MzNbkOWjut/a72CuBW1iS60kIL6AifaqCrtneaRcVEIVBRTL4qz4FRATrn9ODVknSIfO5OgDy1wl15dbZbNP0xIQ" +
            "sxLEoB5M3HNBqRBCDmwqRZ4C5f6KhrmOx6+WHLKyr1rOxvATT6B5QzuCPEZBl2rmU/RcUytjsrlgXxumStR3LRQtR/a/iG9WjDhz2zKEJhdvs3+hruZZYEBk" +
            "h0qLLT21dezHXr0LBYFhoflGM2kP9Rivqo885cT2/hZHJer3Ym429cjgYdSgCHnRN98sJahuyA4aEhpbAOEwuTWYRy5qlI1ZhFeHBADHk8m/UcI7Th9luQEY" +
            "+NRD+/ZHfDEgN3SW5jLBKphblSeFXNRtpuBmjGWCwrtehesOWQS1AhiYNjg9WgWDZihGgAPXNLqEuCJ6MXxLTIKQmgMhtxRJrXj/NVnJvFM8efkSD2cXNonr" +
            "OOV1XIK8LZ1ogIyBvfle7xL+MmGy0feNrxEZ591c0lCrkvQ+IqpkpfXYZvtBZfNUUS86UQ3ewzWhI2vxJNSjQa7gG/Ocddsy5X2bsyGiFlJOSRItOzbkQKlj" +
            "n8FZJX/R5NX5GZYcNIypQlq/r4MkKJ4yi6fgA1R0l8vE0MHF8TPI/mbkQiV6l0fVX5JHBnw5M5VEGxr4EDJWe6MAiDupN/9tusBVN2DT5CbU8omoNY8A1QSU" +
            "ywQsDe6L98c3d49CqTgxO8cLnI3+kcmo2jBEKujgNvi5nNPiGc+aM6jzXttqambY7OzFXBq5kF++jyDQeLR47MBrW4BXwC5t/4NjDYcO9kjZfVo2XhJ/F6GN" +
            "7MAIsqycPZ9CU/J2UZc1cJiRKaKBf1ns/3K+xcf+ycDjvFrH4JVFQl2QbV2xe6XC1WKAUD87n7PtSadVBkISR3HF+hGV+BGmI8bNXiNR5kdCr//Z"),
        new("RpclPrecincts", 40, 36, 3, 8, false, "opj_compress -i RpclPrecincts.ppm -o RpclPrecincts.j2k -n 3 -p RPCL -c [16,16],[8,8],[4,4] -b 8,8 -r 10,3,1",
            "/0//UQAvAAAAAAAoAAAAJAAAAAAAAAAAAAAAKAAAACQAAAAAAAAAAAADBwEBBwEBBwEB/1IADwECAAMBAgEBAAEiM0T/XAAKQEBISFBISFD/ZAAlAAFDcmVh" +
            "dGVkIGJ5IE9wZW5KUEVHIHZlcnNpb24gMi41LjT/kAAKAAAAABLeAAH/k88MElxH325h9EDJIHIB/IGAvJ8vCi5PxwgUAFA9gPziACe0wtgjv8dNxiARUD4c" +
            "wiL84gBeku5U08BUm88KI/MmC0z0ULUjr9/d/IGAj/n0q/WnxwgUAFA2gPziAImX/LXQSyfDxIARUEcP4IBc/OHAxP6QvynreM8IBSziMfQgxSH8gIAsh98I" +
            "gBp9XzbggGD84QBWY6zfgMcIEU7++/zgwEXlXs8MGilPniG9/AKAr3gpDFj8QUARcvi5x/5Q4CTNtXRgubTwQPus/KHAkyNW5tmueMSAEVBDveCAnPzhwL4K" +
            "WNhmHj/PDg/Rc5bARbP0QALJn6z8gYDHpRe0Tzf+IQAYrW2t3OzUxPRAjbhKqvziAMMug5rBY6F//kEAJErRCx2wGwbhAGyh/OIAiaVJKh93EsfOihpJlI/F" +
            "/CEAU7j8QIAHxt8AoBJZBgVK+CCCw/ygwC7xH/5QwBlrlLChzcIm/OEAyCyU78+AMAOrh/zAQMzA3BgL2gv4ED79AIBkn4DHBghpTPzggIZ/xwYL4uqA/OCA" +
            "Az+Az4wgCvUhFfyAQMfcGAIB6vwAgOT84IDQv84IBeH8wEBP9ACAxwQCRfzgQJ+Ax8AQCUr8gEB/gIDA+QVAfCTAfIKAFqSXACJw1H/bFwv63y/2kAhdpx0A" +
            "CooQq2oEMIuAgMHzjIPnGwfUHCJTzp6syrROADIzgw7M93Mel9WdV6moUD8Z3jPM8IPmrKfrTGiA84CAwfOMg+cbB9QcIlPOnsNGAFjvFYkjDs/yJ/QA+Qij" +
            "hl6CYhneM8zwg+juA9Tuovv1gKCIJHDAfCT8wiA+QWAc/3MD8E82+fF02ew0KEho5x+V9HJnbvZtHJefgKIgJHXB8437AyD6g4AU1OQOgZsvwKHnrSfAS8/S" +
            "WSQHsBhdrI2nH69AJfZoQKDtjjTQsyuAgMHzjIPnHQPnHhTU5BcZ09mWidC5fxo5PKuzMaOwzKC8ZrhfE6RLiTuIO+gySWx95/9/gJBhgAENgsHzhoHyBvoC" +
            "gBoymDwt7woFTJtRNzTupLHTgJHBgADgV8Hzh4PnD/aDgBllKRPzfK8Jn6EtN/9fnAlrf9aCf4CAwfOGg+cRA+cQGpHDaYeDDJwA/1sV8q8QzE/5Oibo+IDD" +
            "hIMSHwCgH3XM6wBjjM8kXB33mPziflD/HFCdGzd5hP87XtmtcuX7J9x/vZlbB40VuEZrD8jAGnND+DnwCx4YN9w8GdFjbnUkvqv3Svj9QX9QZ9YYyjSErz1c" +
            "Xw5aiL+VKWh5RpnGbw7ewN8Q+cTfNNHwUjTSUJGAwiREJH0kyP0i4fUH/eHAqjIgg+AH8S9bNw8kxoyVas/THlXctFdP9+9FzP5SERepotOTm33KH8cGACMF" +
            "svhofATD4CSjEDMJiTAG2SZc1g2O7uAecxOIdSBTECf8of5A/xxAzEC9Omh2f21UDvlC1m9gLFAvuU2xv84UFqPntxz4SPER4gDr+vbKB9cQpDVRWRoYiMy1" +
            "SpRM6/0CvoE/WGAmBizBcAakppaPJglKmIdiqX/Hw468WqmJhMkU0Yl/zgwjscL4SEkfAQC+pY9OGEG+LhipRb7cEMW0/QK+kX9QWNIvgafnhh9stRcCBoa7" +
            "gsqF5J3278kDrC2lvBe1zKx3owwPTAjHxhuhQ+AUFe5ohz1f6M0Yx1ElAPyA/nC/OEDCqXeKoXDtP5aREl2nCBD2VX7PiC/ADDhAFpol+CHLF4Zjbf0BPnCf" +
            "UCjdNuwGkggavUaXCD1BpiAMGo6wz4BfgCj4BRaaFA7iHPkVsYjClf1A30BfqBS9xUvchI/6UOpNf4kzqxFPgMPhDj4A4dDAEJG0BtVzDtox/GB+cF9IMHUY" +
            "L8GEr6+AMAbLg8dD4GPgEA+HvGEFzTN+/SC+oC+oCKCYb48Mf4DEU+YHHwBgD6YPoMAQC6L9gH8gL6gM1YGTTC/vr1+AwdDhwcHBgBBecAW/8gMdIfxgfnA/" +
            "OCA/XhwTgMOCjCh8AQADzwNrB9L84L6wL5wYDD/CgIVAd6RADevHxBPiBx0MEF1bG2V0uw8FTPygfnA/YBgBXTHaX4Cg+YEAAwbA+QFBAPhDAnEG/3+AgMD5" +
            "AUD5AUB8gIAMJwavAz+AofSCAAUvwfOCggPkBgLwAV6/gIDAfDRwPkRwHyJAF/eQDZc38lty78Ur93Wf/FJ47iDjP+7Aswc0z2Fa6bej6ecZV4ZP8FpCMrWH" +
            "HfHhEh8nHfCnw4x0V33hd3C5olNnNUIMrxUoTqzczakVeOdloPqQsmaPzb/7Ptfw3bpQkYlVkkjWykW4B4CAwfOyg+dhB9RqDI+pTG+wKSfCG3uNZQSZe9vI" +
            "ZjI1a2vrqJcCrV7Os5UcegrqbWs2CVdacQ8DPC7qHZMR1Rk9sS9vs4ZnyE0/pvfGsXq7Sgwv6HpxZAsj+mgtxEE1ydY+1AC+ljnKSXk/8GI5TZiWcSqCxsnI" +
            "1tAha7e5KJhfuVu4dakN+lwQsYwnVYHPyZnEXJjNX1HLpYHYwqGEuuZqv4CAwfOyg+dhB9RoDI+pTG+wKSfCHPr3sjeaRrFfpr1qXxydw/kXs0jrOVHMPxU1" +
            "g4w4CahV8gRLNxV1DskWIE9+qPARe0V/81F29PrOwyPImJgnbeSRR7gsE/ie2ql7FNyuIsRePcbpq8EuMX85TZiWcSqCxsnI1tAha7e5KJhfuVu4dakUYmd/" +
            "i583VYHPyts4m+0zCkoG0SEOsnsS65mqgJD4gYBicEPA+RHB86X5T4BdJpiSpnKchgd9WO9pWm97jEy7OZcmS87nMjptg4g+8U+ODVyOjcS42se9kRpQ1o+K" +
            "f19raHZM1dKe9uhr8d2Jc2mRwq8MNT//XOBmMbHZq4f7Oeds52nzH9UC8EIulax4ybYuxP9/gMdDRwZicNFibG/9LCH1Gn2mwJkhuG7udigFu2UjGw0kSgsr" +
            "jAa5abEKL7U+rw9UBzIXr1yqDOsZdD+aSd1CzNqVsVx7gur76Dq3KtOLGH8umAgM+7sbxAmqULkOIRk8/wMFQtJ2aB+wthSd05kqT7C9EFdGfp/Rgg2qZYzl" +
            "kOQGJvuhcw+rjZE6Pe3jfRcKQjpmacozhfYOxDQwThV4s7O4wgbb1K1nK/xBIp+AgMHzsoPnZQfUbBSYdSX/gytwGNIBKZSC19tcerKjJQvLIMFcGpdZ4eqA" +
            "2N6mic9R1jLoI1GrRj/UfIcKDkc8Fvgb1Yj92y1fWibE19YMr0QexC2nKK9OvFLwZIMnWjhacMeG5CTVKXLGfn+/eH8V9oWNWLy4sWVMorMRyCgyTSQI8P25" +
            "TCPf6SWv6TpTjkbkKjXFqe53yST37dr5sWJWI1GYkU+Awkhw0EgpnDGrKZ3ADRzIKxUu4f0lfnKfSWBx1ib0Hgdlw7SphbE17TY6iqyXsn/KhCNdLEX3LtUn" +
            "w3NvCcDF+0hNf9B/CDzrIroOH5UzYyJ6JVdO/aoDyz+Azw84aOIAKx21Hl83LSmdwA0c4SX0OdTELBLa/WMfaN/aNpHGqYBk6Oqt9IEkKTtxl3CFEAQCRWKL" +
            "py3QomJJY6kJT1nyLLAVc3th6CsL4o66Ix8iv7SJciCUMWuHleqoydRafxf5BB/skA3K26kev4CREDlOwfOcg+cz942AD0fKJVOkG1ooqQDDOUIm11x5X9+N" +
            "yCBDDHvCjRGGeIg8ZrZ6wBXLNpIq9lyIkEa2EVP6FM9qZs2L4AMDmcee4GOj3AQdcOu73t7G9z41k8uiEGHDpPwEw4pG0gm3QbXW/3T/NdfEkafPwiE5Akb6" +
            "5jrmrQRf/Mh+ZH9RQENr1U4oEWScLvCZ+z5YH7sRviwCKX15JE8ja7jH9tkYi/tGVxXUUAgKBHdJADnIplWpU8BI4olSK00TvZQWolRAIaqY6/Y1jQe9N8wv" +
            "NB53Mg7xuje+nxrU8QXeb5CKlubEy9SshHEtzORlv5JQOR1cMPvPhMni/EBG0gmb0mWGXvtpVvdJ7U1v6OcnSO7qK6eRbkby51C2/Sy+sy+00O8lPn9qMqQX" +
            "B8cl7lwbgJSXkvKgaEbomyBDS2clhJM7APY37ihkFknG/0opHPsTnjhLlamDb6hruYya0vFooAUtGffETe3MMWqLh/qyy3Er5kmwh3SkiRdTH/aeptU3ydBH" +
            "MS9eT7AOQme++u/0xWiVwft8PWF4kQStDNbk9L8oe3UYWisvAw70XDX0fqSWnHh1cBoDI0E/gKfAHERiWcViVsPqNPqM/vNQYkSbUtFYKMhRpVJy0NcQU2W+" +
            "3uoEGMI54kUru4n7gIh4pCbClxZvHNL3VCbS0o5iZI+wBO5dgvLNvzKCYPAEmjyifVsL8EPk3Kn7EAmtHRzB0iw87sB88xxCq1GyhXngB51yW4dDfyxRCZui" +
            "OUohTpR15Kp7RB0CNFwqaoIvtylqzRHLZ/VsDc/0cX3PgzEAPgreFOerq9M/a3x/xMA5AjxNNRH5CPhUj4zg24NNidfNqJ1xUYc6XUFnwD5cJFdK4o3s0CkK" +
            "RMhTctmM416aOOhuKf6xiFqYaNNCx62clx1bG9hhJzKuPIUKHfzpvmTfOiBlSc2ZCHC1rS+/e5T7pJTzK/XEtTQyfIJHVPvL/SuEnCpzu0DUSx4K1NDn2KHo" +
            "vtEhoXKGdmMpl5iNt2jS+/qvym68m2m3qiQuCaKHh82oa9b4H/QleU9hKQ9xwhhfTUNMc6J/NaaJ+t1n+De8LMqUVYAmLoxxyQDqbKcCHIfmRDI/A2S87PiE" +
            "+FbhIKAQWtQ0OIuIDd5od9D/OqkA3zyjQXOcoFrj8Brn7+ZgZTd730en3w3h7UibSYn7/ULvpXfYNTjX9Tnvxs99g2lf50N8IBzoVoZX79H0/ZAFQuUNYeKN" +
            "ufvUOMsG3uxTZDJDHPf/KGY0h44dhY4BB9wu7/YfgSpC2X/MFuLUMLOTkKNPzb77Wbrd1eCznlgridEnescn5Yk575Szxe7BX52t8KqOIrSqAgYZhnHR/qJ4" +
            "10G8NY911PIhUMp1hRZsUjOiRB4kEZeAz4T58J8dNDhcaP32eUiTXVNxFEdKXj20Jy5xE7M3dr1T8sHUnDespB9b+vLCXOiyR0T9LH6WP2GoBQ4sZFDVrO1+" +
            "KXS4KoTt3B10caoNOSAn199UOUS0YRsLnkmq317Z+O4CUSCcf+hjn1TwTscpgiFEQPoDTFAnVsuujIaXEgQZR9BU4tCrEEVey0lBFYVRTxs6groC3IiXHJ0N" +
            "NXKeYaK2DQRubMB0FrJKNZCfCQMxSD8qR0ARG/c/kXHnDSbvvd6rHlTUlArXG2SqtCsI5KJAAtSc4MfAX8BEPgPAIb4s59H/dcWBDMSyLQg1Wc/dSAnVcSao" +
            "75udY0Vy5nERdfzlPmKfORCs7x+ZF2bou8LuusBUelOmZIkQBw/NmiD4lETOGR4YUKfX1xmCa/a/jFTItiT9Bx25HvkA40MhZU+Azw88RHhwImoOpRu3wAAk" +
            "lOCyLT3+BynNaBAH8v1jX1jX1jKmxjesy+mMbs4TNgmzKxUcn85b8IRoEwRTdn+lWsogKt2RwFaWqQrxUDHCcE0uFimxFmB/6SuiOeChCVaBWXK5AXUlh4u6" +
            "9pYFEL5mH5MUKSKclxzPgOnwHfAUIioPFr/M04XBu2KqSjAAJJRy+3CCFtdXbVmLYGD5784VZsa5Kgr9Qv8oY+sWR8scEi2DHR8M8bDDnli9vHkPOoYANt/9" +
            "DeaxBUhuYrrM0eBO1p11dvEdDXZuSL9zYL9w+InnOoyzEYMZUnLOcYwqB1K/xGAdJhn8AkOjx8gkvJuYQgUD2I3zIhkhVhhjRHX7uXr8wr5hPzCQSEj38Zux" +
            "37Q2P/rWrHStwBFrf20FEDNrduZ1L4DPgInh48QADZSx3C83QaQFC3fhm0G9BII6cgIjVf79QZ9YZ9YYEQgkzychsDt6dWPVVYNE/kn1th3i/Fe/vooheKmN" +
            "UwJUMVJ/gM+AafALHhgcT0P9bxsk/3rMyhgOJFNzHv1Bn1Bn1hh4apLnJIv+9CPi9L9kkWq94aq4BZpNyn+P1ZG11PJDylJjhe2Ax8IsOFh8A4AFTaokdAep" +
            "bqNKGbuES1jiLPzC/nF/OHApgGfICZaV04xHf9ZwEi2bWSYNkKR/riotGP7m74DPjBz4AwAIBMoFVHf84z6g2D6g4Kwkhx9JKSstYOialRM2odX9RkxSgRJm" +
            "kN8E+MiE6Oyahypkn0LIf4DPDTho4UAHnFDT4OME8ecj+0wXYO/Jq/1hn2hv2hjwVfpHDpwN5psj9i/hSE4Uy1nfjPuGx/h/qdaSFx2GFTRLVhocgIDA+QPA" +
            "fCJAfIHABO+WocJjfw7aNFYSphBmnP9/gIDB84eB8gaD6ggJzLoioHkhAhLx4Rp/D9ADLjAqqgGAgMPqCIHyBoPqCAT5Olt053PiAhLx4Rp/D9ADLjAqqgH/" +
            "2Q=="),
        new("PcrlPrecincts", 40, 36, 3, 8, false, "opj_compress -i PcrlPrecincts.ppm -o PcrlPrecincts.j2k -n 3 -p PCRL -c [16,16],[8,8],[4,4] -b 8,8 -r 10,3,1",
            "/0//UQAvAAAAAAAoAAAAJAAAAAAAAAAAAAAAKAAAACQAAAAAAAAAAAADBwEBBwEBBwEB/1IADwEDAAMBAgEBAAEiM0T/XAAKQEBISFBISFD/ZAAlAAFDcmVh" +
            "dGVkIGJ5IE9wZW5KUEVHIHZlcnNpb24gMi41LjT/kAAKAAAAABLeAAH/k88MElxH325h9EDJIHIB/IGAvJ8vCi5PgIDA+QVAfCTAfIKAFqSXACJw1H/bFwv6" +
            "3y/2kAhdpx0ACooQq2oEMIuAgMB8NHA+RHAfIkAX95ANlzfyW3LvxSv3dZ/8UnjuIOM/7sCzBzTPYVrpt6Pp5xlXhk/wWkIytYcd8eESHycd8KfDjHRXfeF3" +
            "cLmiU2c1QgyvFShOrNzNqRV452Wg+pCyZo/Nv/s+1/DdulCRiVWSSNbKRbgHxwgUAFA9gPziACe0wtgjv8dNgIDB84yD5xsH1BwiU86erMq0TgAyM4MOzPdz" +
            "HpfVnVepqFA/Gd4zzPCD5qyn60xogPOAgMHzsoPnYQfUagyPqUxvsCknwht7jWUEmXvbyGYyNWtr66iXAq1ezrOVHHoK6m1rNglXWnEPAzwu6h2TEdUZPbEv" +
            "b7OGZ8hNP6b3xrF6u0oML+h6cWQLI/poLcRBNcnWPtQAvpY5ykl5P/BiOU2YlnEqgsbJyNbQIWu3uSiYX7lbuHWpDfpcELGMJ1WBz8mZxFyYzV9Ry6WB2MKh" +
            "hLrmar/GIBFQPhzCIvziAF6S7lTTwFSbgIDB84yD5xsH1BwiU86ew0YAWO8ViSMOz/In9AD5CKOGXoJiGd4zzPCD6O4D1O6i+/WAgMHzsoPnYQfUaAyPqUxv" +
            "sCknwhz697I3mkaxX6a9al8cncP5F7NI6zlRzD8VNYOMOAmoVfIESzcVdQ7JFiBPfqjwEXtFf/NRdvT6zsMjyJiYJ23kkUe4LBP4ntqpexTcriLEXj3G6avB" +
            "LjF/OU2YlnEqgsbJyNbQIWu3uSiYX7lbuHWpFGJnf4ufN1WBz8rbOJvtMwpKBtEhDrJ7EuuZqs8KI/MmC0z0ULUjr9/d/IGAj/n0q/WngKCIJHDAfCT8wiA+" +
            "QWAc/3MD8E82+fF02ew0KEho5x+V9HJnbvZtHJefgJD4gYBicEPA+RHB86X5T4BdJpiSpnKchgd9WO9pWm97jEy7OZcmS87nMjptg4g+8U+ODVyOjcS42se9" +
            "kRpQ1o+Kf19raHZM1dKe9uhr8d2Jc2mRwq8MNT//XOBmMbHZq4f7Oeds52nzH9UC8EIulax4ybYuxP9/xwgUAFA2gPziAImX/LXQSyfDgKIgJHXB8437AyD6" +
            "g4AU1OQOgZsvwKHnrSfAS8/SWSQHsBhdrI2nH69AJfZoQKDtjjTQsyuAx0NHBmJw0WJsb/0sIfUafabAmSG4bu52KAW7ZSMbDSRKCyuMBrlpsQovtT6vD1QH" +
            "MhevXKoM6xl0P5pJ3ULM2pWxXHuC6vvoOrcq04sYfy6YCAz7uxvECapQuQ4hGTz/AwVC0nZoH7C2FJ3TmSpPsL0QV0Z+n9GCDapljOWQ5AYm+6FzD6uNkTo9" +
            "7eN9FwpCOmZpyjOF9g7ENDBOFXizs7jCBtvUrWcr/EEin8SAEVBHD+CAXPzhwMT+kL8p63iAgMHzjIPnHQPnHhTU5BcZ09mWidC5fxo5PKuzMaOwzKC8Zrhf" +
            "E6RLiTuIO+gySWx95/9/gIDB87KD52UH1GwUmHUl/4MrcBjSASmUgtfbXHqyoyULyyDBXBqXWeHqgNjeponPUdYy6CNRq0Y/1HyHCg5HPBb4G9WI/dstX1om" +
            "xNfWDK9EHsQtpyivTrxS8GSDJ1o4WnDHhuQk1Slyxn5/v3h/FfaFjVi8uLFlTKKzEcgoMk0kCPD9uUwj3+klr+k6U45G5Co1xanud8kk9+3a+bFiViNRmJFP" +
            "zwgFLOIx9CDFIfyAgCyHgJBhgAENgsHzhoHyBvoCgBoymDwt7woFTJtRNzTupLHTgMJIcNBIKZwxqymdwA0cyCsVLuH9JX5yn0lgcdYm9B4HZcO0qYWxNe02" +
            "Ooqsl7J/yoQjXSxF9y7VJ8NzbwnAxftITX/Qfwg86yK6Dh+VM2MieiVXTv2qA8s/3wiAGn1fNuCAYPzhAFZjrN+AkcGAAOBXwfOHg+cP9oOAGWUpE/N8rwmf" +
            "oS03/1+cCWt/1oJ/gM8POGjiACsdtR5fNy0pncANHOEl9DnUxCwS2v1jH2jf2jaRxqmAZOjqrfSBJCk7cZdwhRAEAkVii6ct0KJiSWOpCU9Z8iywFXN7Yegr" +
            "C+KOuiMfIr+0iXIglDFrh5XqqMnUWn8X+QQf7JANytupHr+AxwgRTv77/ODAReVegIDB84aD5xED5xAakcNph4MMnAD/WxXyrxDMT/k6Juj4gJEQOU7B85yD" +
            "5zP3jYAPR8olU6QbWiipAMM5QibXXHlf343IIEMMe8KNEYZ4iDxmtnrAFcs2kir2XIiQRrYRU/oUz2pmzYvgAwOZx57gY6PcBB1w67ve3sb3PjWTy88MGilP" +
            "niG9/AKAr3gpDFj8QUARcvi5x4DDhIMSHwCgH3XM6wBjjM8kXB33mPziflD/HFCdGzd5hP87XtmtcuX7J9x/vZlbB40VuEZrD6IQYcOk/ATDikbSCbdBtdb/" +
            "dP8118SRp8/CITkCRvrmOuatBF/8yH5kf1FAQ2vVTigRZJwu8Jn7PlgfuxG+LAIpfXkkTyNruMf22RiL+0ZXFdRQCAoEd0kAOcimValTwEjiiVIrTRO9lBai" +
            "VEAhqpjr9jWNB703zC80HncyDvG6N76fGtTxBd5vkIqW5sTL1KyEcS3M5GW//lDgJM21dGC5tPBA+6z8ocCTI1bm2a54yMAac0P4OfALHhg33DwZ0WNudSS+" +
            "q/dK+P1Bf1Bn1hjKNISvPVxfDlqIv5UpaHlGmcZvDt7A3xD5xN800fBSNNJQkZJQOR1cMPvPhMni/EBG0gmb0mWGXvtpVvdJ7U1v6OcnSO7qK6eRbkby51C2" +
            "/Sy+sy+00O8lPn9qMqQXB8cl7lwbgJSXkvKgaEbomyBDS2clhJM7APY37ihkFknG/0opHPsTnjhLlamDb6hruYya0vFooAUtGffETe3MMWqLh/qyy3Er5kmw" +
            "h3SkiRdTH/aeptU3ydBHMS9eT7AOQme++u/0xWiVwft8PWF4kQStDNbk9L8oe3UYWisvAw70XDX0fqSWnHh1cBoDI0E/xIARUEO94ICc/OHAvgpY2GYeP4DC" +
            "JEQkfSTI/SLh9Qf94cCqMiCD4AfxL1s3DyTGjJVqz9MeVdy0V0/370XM/lIRF6mi05ObfcofgKfAHERiWcViVsPqNPqM/vNQYkSbUtFYKMhRpVJy0NcQU2W+" +
            "3uoEGMI54kUru4n7gIh4pCbClxZvHNL3VCbS0o5iZI+wBO5dgvLNvzKCYPAEmjyifVsL8EPk3Kn7EAmtHRzB0iw87sB88xxCq1GyhXngB51yW4dDfyxRCZui" +
            "OUohTpR15Kp7RB0CNFwqaoIvtylqzRHLZ/VsDc/0cX3PgzEAPgreFOerq9M/a3x/zw4P0XOWwEWz9EACyZ+s/IGAx6UXtE83xwYAIwWy+Gh8BMPgJKMQMwmJ" +
            "MAbZJlzWDY7u4B5zE4h1IFMQJ/yh/kD/HEDMQL06aHZ/bVQO+ULWb2AsUC+5TbG/xMA5AjxNNRH5CPhUj4zg24NNidfNqJ1xUYc6XUFnwD5cJFdK4o3s0CkK" +
            "RMhTctmM416aOOhuKf6xiFqYaNNCx62clx1bG9hhJzKuPIUKHfzpvmTfOiBlSc2ZCHC1rS+/e5T7pJTzK/XEtTQyfIJHVPvL/SuEnCpzu0DUSx4K1NDn2KHo" +
            "vtEhoXKGdmMpl5iNt2jS+/qvym68m2m3qiQuCaKHh82oa9b4H/QleU9hKQ9xwhhfTUNMc6J/NaaJ+t1n+De8LP4hABitba3c7NTE9ECNuEqq/OIAwy6DmsFj" +
            "oX/OFBaj57cc+EjxEeIA6/r2ygfXEKQ1UVkaGIjMtUqUTOv9Ar6BP1hgJgYswXAGpKaWjyYJSpiHYql/x8OOvFqpiYTJFNGJf8qUVYAmLoxxyQDqbKcCHIfm" +
            "RDI/A2S87PiE+FbhIKAQWtQ0OIuIDd5od9D/OqkA3zyjQXOcoFrj8Brn7+ZgZTd730en3w3h7UibSYn7/ULvpXfYNTjX9Tnvxs99g2lf50N8IBzoVoZX79H0" +
            "/ZAFQuUNYeKNufvUOMsG3uxTZDJDHPf/KGY0h44dhY4BB9wu7/YfgSpC2X/MFuLUMLOTkKNPzb77Wbrd1eCznlgridEnescn5Yk575Szxe7BX52t8KqOIrSq" +
            "AgYZhnHR/qJ410G8NY911PIhUMp1hRZsUjOiRB4kEZf+QQAkStELHbAbBuEAbKH84gCJpUkqH3cSx84MI7HC+EhJHwEAvqWPThhBvi4YqUW+3BDFtP0CvpF/" +
            "UFjSL4Gn54YfbLUXAgaGu4LKheSd9u/JA6wtpbwXtcysd4DPhPnwnx00OFxo/fZ5SJNdU3EUR0pePbQnLnETszd2vVPywdScN6ykH1v68sJc6LJHRP0sfpY/" +
            "YagFDixkUNWs7X4pdLgqhO3cHXRxqg05ICfX31Q5RLRhGwueSarfXtn47gJRIJx/6GOfVPBOxymCIURA+gNMUCdWy66MhpcSBBlH0FTi0KsQRV7LSUEVhVFP" +
            "GzqCugLciJccnQ01cp5horYNBG5swHQWsko1kJ8JAzFIPypHQBEb9z+RcecNJu+93qseVNSUCtcbZKq0KwjkzooaSZSPxfwhAFO4/ECAB8ajDA9MCMfGG6FD" +
            "4BQV7miHPV/ozRjHUSUA/ID+cL84QMKpd4qhcO0/lpESXaJAAtSc4MfAX8BEPgPAIb4s59H/dcWBDMSyLQg1Wc/dSAnVcSao75udY0Vy5nERdfzlPmKfORCs" +
            "7x+ZF2bou8LuusBUelOmZIkQBw/NmiD4lETOGR4YUKfX1xmCa/a/jFTItiT9Bx25HvkA40MhZU/fAKASWQYFSvgggsP8oMAu8R+nCBD2VX7PiC/ADDhAFpol" +
            "+CHLF4Zjbf0BPnCfUCjdNuwGkggavUaXCD1BgM8PPER4cCJqDqUbt8AAJJTgsi09/gcpzWgQB/L9Y19Y19YypsY3rMvpjG7OEzYJsysVHJ/OW/CEaBMEU3Z/" +
            "pVrKICrdkcBWlqkK8VAxwnBNLhYpsRZgf+krojngoQlWgVlyuQF1JYeLuvaWBRC+Zh/+UMAZa5Swoc3CJvzhAMgslO+mIAwajrDPgF+AKPgFFpoUDuIc+RWx" +
            "iMKV/UDfQF+oFL3FS9yEj/pQ6k1/iTOrEU+TFCkinJccz4Dp8B3wFCIqDxa/zNOFwbtiqkowACSUcvtwghbXV21Zi2Bg+e/OFWbGuSoK/UL/KGPrFkfLHBIt" +
            "gx0fDPGww55Yvbx5DzqGADbf/Q3msQVIbmK6zNHgTtaddXbxHQ12bki/c2C/cPiJ5zqMsxGDGVJyznGMKgdSv8+AMAOrh/zAQMzAgMPhDj4A4dDAEJG0BtVz" +
            "Dtox/GB+cF9IMHUYL8GEr8RgHSYZ/AJDo8fIJLybmEIFA9iN8yIZIVYYY0R1+7l6/MK+YT8wkEhI9/Gbsd+0Nj/61qx0rcARa39tBRAza3bmdS/cGAvaC/gQ" +
            "Pv0AgGSfr4AwBsuDx0PgY+AQD4e8YQXNM379IL6gL6gIoJhvjwx/gM+AieHjxAANlLHcLzdBpAULd+GbQb0EgjpyAiNV/v1Bn1hn1hgRCCTPJyGwO3p1Y9VV" +
            "g0T+SfW2HeL8V7++iiF4qY1TAlQxUn+AxwYIaUz84ICGf4DEU+YHHwBgD6YPoMAQC6L9gH8gL6gM1YGTTC/vr1+Az4Bp8AseGBxPQ/1vGyT/eszKGA4kU3Me" +
            "/UGfUGfWGHhqkucki/70I+L0v2SRar3hqrgFmk3Kf4/VkbXU8kPKUmOF7ccGC+LqgPzggAM/gMHQ4cHBwYAQXnAFv/IDHSH8YH5wPzggP14cE4DHwiw4WHwD" +
            "gAVNqiR0B6luo0oZu4RLWOIs/ML+cX84cCmAZ8gJlpXTjEd/1nASLZtZJg2QpH+uKi0Y/ubvgM+MIAr1IRX8gEDHgMOCjCh8AQADzwNrB9L84L6wL5wYDD/C" +
            "gIVAd4DPjBz4AwAIBMoFVHf84z6g2D6g4Kwkhx9JKSstYOialRM2odX9RkxSgRJmkN8E+MiE6Oyahypkn0LIf9wYAgHq/ACA5PzggNC/pEAN68fEE+IHHQwQ" +
            "XVsbZXS7DwVM/KB+cD9gGAFdMdpfgM8NOGjhQAecUNPg4wTx5yP7TBdg78mr/WGfaG/aGPBV+kcOnA3mmyP2L+FIThTLWd+M+4bH+H+p1pIXHYYVNEtWGhzO" +
            "CAXh/MBAT/QAgKD5gQADBsD5AUEA+EMCcQb/f4CAwPkDwHwiQHyBwATvlqHCY38O2jRWEqYQZpz/f4DHBAJF/OBAn4CAwPkBQPkBQHyAgAwnBq8DP4CAwfOH" +
            "gfIGg+oICcy6IqB5IQIS8eEafw/QAy4wKqoBgMfAEAlK/IBAf4Ch9IIABS/B84KCA+QGAvABXr+AgMPqCIHyBoPqCAT5Olt053PiAhLx4Rp/D9ADLjAqqgH/" +
            "2Q=="),
        new("CprlPrecincts", 40, 36, 3, 8, false, "opj_compress -i CprlPrecincts.ppm -o CprlPrecincts.j2k -n 3 -p CPRL -c [16,16],[8,8],[4,4] -b 8,8 -r 10,3,1",
            "/0//UQAvAAAAAAAoAAAAJAAAAAAAAAAAAAAAKAAAACQAAAAAAAAAAAADBwEBBwEBBwEB/1IADwEEAAMBAgEBAAEiM0T/XAAKQEBISFBISFD/ZAAlAAFDcmVh" +
            "dGVkIGJ5IE9wZW5KUEVHIHZlcnNpb24gMi41LjT/kAAKAAAAABLeAAH/k88MElxH325h9EDJIHIB/IGAvJ8vCi5PgIDA+QVAfCTAfIKAFqSXACJw1H/bFwv6" +
            "3y/2kAhdpx0ACooQq2oEMIuAgMB8NHA+RHAfIkAX95ANlzfyW3LvxSv3dZ/8UnjuIOM/7sCzBzTPYVrpt6Pp5xlXhk/wWkIytYcd8eESHycd8KfDjHRXfeF3" +
            "cLmiU2c1QgyvFShOrNzNqRV452Wg+pCyZo/Nv/s+1/DdulCRiVWSSNbKRbgHzwoj8yYLTPRQtSOv3938gYCP+fSr9aeAoIgkcMB8JPzCID5BYBz/cwPwTzb5" +
            "8XTZ7DQoSGjnH5X0cmdu9m0cl5+AkPiBgGJwQ8D5EcHzpflPgF0mmJKmcpyGB31Y72lab3uMTLs5lyZLzucyOm2DiD7xT44NXI6NxLjax72RGlDWj4p/X2to" +
            "dkzV0p726Gvx3YlzaZHCrww1P/9c4GYxsdmrh/s552znafMf1QLwQi6VrHjJti7E/3/PCAUs4jH0IMUh/ICALIeAkGGAAQ2CwfOGgfIG+gKAGjKYPC3vCgVM" +
            "m1E3NO6ksdOAwkhw0EgpnDGrKZ3ADRzIKxUu4f0lfnKfSWBx1ib0Hgdlw7SphbE17TY6iqyXsn/KhCNdLEX3LtUnw3NvCcDF+0hNf9B/CDzrIroOH5UzYyJ6" +
            "JVdO/aoDyz/PDBopT54hvfwCgK94KQxY/EFAEXL4uceAw4SDEh8AoB91zOsAY4zPJFwd95j84n5Q/xxQnRs3eYT/O17ZrXLl+yfcf72ZWweNFbhGaw+iEGHD" +
            "pPwEw4pG0gm3QbXW/3T/NdfEkafPwiE5Akb65jrmrQRf/Mh+ZH9RQENr1U4oEWScLvCZ+z5YH7sRviwCKX15JE8ja7jH9tkYi/tGVxXUUAgKBHdJADnIplWp" +
            "U8BI4olSK00TvZQWolRAIaqY6/Y1jQe9N8wvNB53Mg7xuje+nxrU8QXeb5CKlubEy9SshHEtzORlv88OD9FzlsBFs/RAAsmfrPyBgMelF7RPN8cGACMFsvho" +
            "fATD4CSjEDMJiTAG2SZc1g2O7uAecxOIdSBTECf8of5A/xxAzEC9Omh2f21UDvlC1m9gLFAvuU2xv8TAOQI8TTUR+Qj4VI+M4NuDTYnXzaidcVGHOl1BZ8A+" +
            "XCRXSuKN7NApCkTIU3LZjONemjjobin+sYhamGjTQsetnJcdWxvYYScyrjyFCh386b5k3zogZUnNmQhwta0vv3uU+6SU8yv1xLU0MnyCR1T7y/0rhJwqc7tA" +
            "1EseCtTQ59ih6L7RIaFyhnZjKZeYjbdo0vv6r8puvJtpt6okLgmih4fNqGvW+B/0JXlPYSkPccIYX01DTHOifzWmifrdZ/g3vCzOihpJlI/F/CEAU7j8QIAH" +
            "xqMMD0wIx8YboUPgFBXuaIc9X+jNGMdRJQD8gP5wvzhAwql3iqFw7T+WkRJdokAC1Jzgx8BfwEQ+A8Ahvizn0f91xYEMxLItCDVZz91ICdVxJqjvm51jRXLm" +
            "cRF1/OU+Yp85EKzvH5kXZui7wu66wFR6U6ZkiRAHD82aIPiURM4ZHhhQp9fXGYJr9r+MVMi2JP0HHbke+QDjQyFlT8+AMAOrh/zAQMzAgMPhDj4A4dDAEJG0" +
            "BtVzDtox/GB+cF9IMHUYL8GEr8RgHSYZ/AJDo8fIJLybmEIFA9iN8yIZIVYYY0R1+7l6/MK+YT8wkEhI9/Gbsd+0Nj/61qx0rcARa39tBRAza3bmdS/HBgvi" +
            "6oD84IADP4DB0OHBwcGAEF5wBb/yAx0h/GB+cD84ID9eHBOAx8IsOFh8A4AFTaokdAepbqNKGbuES1jiLPzC/nF/OHApgGfICZaV04xHf9ZwEi2bWSYNkKR/" +
            "riotGP7m784IBeH8wEBP9ACAoPmBAAMGwPkBQQD4QwJxBv9/gIDA+QPAfCJAfIHABO+WocJjfw7aNFYSphBmnP9/xwgUAFA9gPziACe0wtgjv8dNgIDB84yD" +
            "5xsH1BwiU86erMq0TgAyM4MOzPdzHpfVnVepqFA/Gd4zzPCD5qyn60xogPOAgMHzsoPnYQfUagyPqUxvsCknwht7jWUEmXvbyGYyNWtr66iXAq1ezrOVHHoK" +
            "6m1rNglXWnEPAzwu6h2TEdUZPbEvb7OGZ8hNP6b3xrF6u0oML+h6cWQLI/poLcRBNcnWPtQAvpY5ykl5P/BiOU2YlnEqgsbJyNbQIWu3uSiYX7lbuHWpDfpc" +
            "ELGMJ1WBz8mZxFyYzV9Ry6WB2MKhhLrmar/HCBQAUDaA/OIAiZf8tdBLJ8OAoiAkdcHzjfsDIPqDgBTU5A6Bmy/AoeetJ8BLz9JZJAewGF2sjacfr0Al9mhA" +
            "oO2ONNCzK4DHQ0cGYnDRYmxv/Swh9Rp9psCZIbhu7nYoBbtlIxsNJEoLK4wGuWmxCi+1Pq8PVAcyF69cqgzrGXQ/mkndQszalbFce4Lq++g6tyrTixh/LpgI" +
            "DPu7G8QJqlC5DiEZPP8DBULSdmgfsLYUndOZKk+wvRBXRn6f0YINqmWM5ZDkBib7oXMPq42ROj3t430XCkI6ZmnKM4X2DsQ0ME4VeLOzuMIG29StZyv8QSKf" +
            "3wiAGn1fNuCAYPzhAFZjrN+AkcGAAOBXwfOHg+cP9oOAGWUpE/N8rwmfoS03/1+cCWt/1oJ/gM8POGjiACsdtR5fNy0pncANHOEl9DnUxCwS2v1jH2jf2jaR" +
            "xqmAZOjqrfSBJCk7cZdwhRAEAkVii6ct0KJiSWOpCU9Z8iywFXN7YegrC+KOuiMfIr+0iXIglDFrh5XqqMnUWn8X+QQf7JANytupHr/+UOAkzbV0YLm08ED7" +
            "rPyhwJMjVubZrnjIwBpzQ/g58AseGDfcPBnRY251JL6r90r4/UF/UGfWGMo0hK89XF8OWoi/lSloeUaZxm8O3sDfEPnE3zTR8FI00lCRklA5HVww+8+EyeL8" +
            "QEbSCZvSZYZe+2lW90ntTW/o5ydI7uorp5FuRvLnULb9LL6zL7TQ7yU+f2oypBcHxyXuXBuAlJeS8qBoRuibIENLZyWEkzsA9jfuKGQWScb/Sikc+xOeOEuV" +
            "qYNvqGu5jJrS8WigBS0Z98RN7cwxaouH+rLLcSvmSbCHdKSJF1Mf9p6m1TfJ0EcxL15PsA5CZ7767/TFaJXB+3w9YXiRBK0M1uT0vyh7dRhaKy8DDvRcNfR+" +
            "pJaceHVwGgMjQT/+IQAYrW2t3OzUxPRAjbhKqvziAMMug5rBY6F/zhQWo+e3HPhI8RHiAOv69soH1xCkNVFZGhiIzLVKlEzr/QK+gT9YYCYGLMFwBqSmlo8m" +
            "CUqYh2Kpf8fDjrxaqYmEyRTRiX/KlFWAJi6McckA6mynAhyH5kQyPwNkvOz4hPhW4SCgEFrUNDiLiA3eaHfQ/zqpAN88o0FznKBa4/Aa5+/mYGU3e99Hp98N" +
            "4e1Im0mJ+/1C76V32DU41/U578bPfYNpX+dDfCAc6FaGV+/R9P2QBULlDWHijbn71DjLBt7sU2QyQxz3/yhmNIeOHYWOAQfcLu/2H4EqQtl/zBbi1DCzk5Cj" +
            "T82++1m63dXgs55YK4nRJ3rHJ+WJOe+Us8XuwV+drfCqjiK0qgIGGYZx0f6ieNdBvDWPddTyIVDKdYUWbFIzokQeJBGX3wCgElkGBUr4IILD/KDALvEfpwgQ" +
            "9lV+z4gvwAw4QBaaJfghyxeGY239AT5wn1Ao3TbsBpIIGr1Glwg9QYDPDzxEeHAiag6lG7fAACSU4LItPf4HKc1oEAfy/WNfWNfWMqbGN6zL6YxuzhM2CbMr" +
            "FRyfzlvwhGgTBFN2f6VayiAq3ZHAVpapCvFQMcJwTS4WKbEWYH/pK6I54KEJVoFZcrkBdSWHi7r2lgUQvmYf3BgL2gv4ED79AIBkn6+AMAbLg8dD4GPgEA+H" +
            "vGEFzTN+/SC+oC+oCKCYb48Mf4DPgInh48QADZSx3C83QaQFC3fhm0G9BII6cgIjVf79QZ9YZ9YYEQgkzychsDt6dWPVVYNE/kn1th3i/Fe/vooheKmNUwJU" +
            "MVJ/gM+MIAr1IRX8gEDHgMOCjCh8AQADzwNrB9L84L6wL5wYDD/CgIVAd4DPjBz4AwAIBMoFVHf84z6g2D6g4Kwkhx9JKSstYOialRM2odX9RkxSgRJmkN8E" +
            "+MiE6Oyahypkn0LIf4DHBAJF/OBAn4CAwPkBQPkBQHyAgAwnBq8DP4CAwfOHgfIGg+oICcy6IqB5IQIS8eEafw/QAy4wKqoBxiARUD4cwiL84gBeku5U08BU" +
            "m4CAwfOMg+cbB9QcIlPOnsNGAFjvFYkjDs/yJ/QA+Qijhl6CYhneM8zwg+juA9Tuovv1gIDB87KD52EH1GgMj6lMb7ApJ8Ic+veyN5pGsV+mvWpfHJ3D+Rez" +
            "SOs5Ucw/FTWDjDgJqFXyBEs3FXUOyRYgT36o8BF7RX/zUXb0+s7DI8iYmCdt5JFHuCwT+J7aqXsU3K4ixF49xumrwS4xfzlNmJZxKoLGycjW0CFrt7komF+5" +
            "W7h1qRRiZ3+LnzdVgc/K2zib7TMKSgbRIQ6yexLrmarEgBFQRw/ggFz84cDE/pC/Ket4gIDB84yD5x0D5x4U1OQXGdPZlonQuX8aOTyrszGjsMygvGa4XxOk" +
            "S4k7iDvoMklsfef/f4CAwfOyg+dlB9RsFJh1Jf+DK3AY0gEplILX21x6sqMlC8sgwVwal1nh6oDY3qaJz1HWMugjUatGP9R8hwoORzwW+BvViP3bLV9aJsTX" +
            "1gyvRB7ELacor068UvBkgydaOFpwx4bkJNUpcsZ+f794fxX2hY1YvLixZUyisxHIKDJNJAjw/blMI9/pJa/pOlOORuQqNcWp7nfJJPft2vmxYlYjUZiRT4DH" +
            "CBFO/vv84MBF5V6AgMHzhoPnEQPnEBqRw2mHgwycAP9bFfKvEMxP+Tom6PiAkRA5TsHznIPnM/eNgA9HyiVTpBtaKKkAwzlCJtdceV/fjcggQwx7wo0RhniI" +
            "PGa2esAVyzaSKvZciJBGthFT+hTPambNi+ADA5nHnuBjo9wEHXDru97exvc+NZPLxIARUEO94ICc/OHAvgpY2GYeP4DCJEQkfSTI/SLh9Qf94cCqMiCD4Afx" +
            "L1s3DyTGjJVqz9MeVdy0V0/370XM/lIRF6mi05ObfcofgKfAHERiWcViVsPqNPqM/vNQYkSbUtFYKMhRpVJy0NcQU2W+3uoEGMI54kUru4n7gIh4pCbClxZv" +
            "HNL3VCbS0o5iZI+wBO5dgvLNvzKCYPAEmjyifVsL8EPk3Kn7EAmtHRzB0iw87sB88xxCq1GyhXngB51yW4dDfyxRCZuiOUohTpR15Kp7RB0CNFwqaoIvtylq" +
            "zRHLZ/VsDc/0cX3PgzEAPgreFOerq9M/a3x//kEAJErRCx2wGwbhAGyh/OIAiaVJKh93EsfODCOxwvhISR8BAL6lj04YQb4uGKlFvtwQxbT9Ar6Rf1BY0i+B" +
            "p+eGH2y1FwIGhruCyoXknfbvyQOsLaW8F7XMrHeAz4T58J8dNDhcaP32eUiTXVNxFEdKXj20Jy5xE7M3dr1T8sHUnDespB9b+vLCXOiyR0T9LH6WP2GoBQ4s" +
            "ZFDVrO1+KXS4KoTt3B10caoNOSAn199UOUS0YRsLnkmq317Z+O4CUSCcf+hjn1TwTscpgiFEQPoDTFAnVsuujIaXEgQZR9BU4tCrEEVey0lBFYVRTxs6groC" +
            "3IiXHJ0NNXKeYaK2DQRubMB0FrJKNZCfCQMxSD8qR0ARG/c/kXHnDSbvvd6rHlTUlArXG2SqtCsI5P5QwBlrlLChzcIm/OEAyCyU76YgDBqOsM+AX4Ao+AUW" +
            "mhQO4hz5FbGIwpX9QN9AX6gUvcVL3ISP+lDqTX+JM6sRT5MUKSKclxzPgOnwHfAUIioPFr/M04XBu2KqSjAAJJRy+3CCFtdXbVmLYGD5784VZsa5Kgr9Qv8o" +
            "Y+sWR8scEi2DHR8M8bDDnli9vHkPOoYANt/9DeaxBUhuYrrM0eBO1p11dvEdDXZuSL9zYL9w+InnOoyzEYMZUnLOcYwqB1K/gMcGCGlM/OCAhn+AxFPmBx8A" +
            "YA+mD6DAEAui/YB/IC+oDNWBk0wv769fgM+AafALHhgcT0P9bxsk/3rMyhgOJFNzHv1Bn1Bn1hh4apLnJIv+9CPi9L9kkWq94aq4BZpNyn+P1ZG11PJDylJj" +
            "he3cGAIB6vwAgOT84IDQv6RADevHxBPiBx0MEF1bG2V0uw8FTPygfnA/YBgBXTHaX4DPDTho4UAHnFDT4OME8ecj+0wXYO/Jq/1hn2hv2hjwVfpHDpwN5psj" +
            "9i/hSE4Uy1nfjPuGx/h/qdaSFx2GFTRLVhocgMfAEAlK/IBAf4Ch9IIABS/B84KCA+QGAvABXr+AgMPqCIHyBoPqCAT5Olt053PiAhLx4Rp/D9ADLjAqqgH/" +
            "2Q=="),
        new("RpclTiledOffsets", 37, 29, 3, 8, false, "opj_compress -i RpclTiledOffsets.ppm -o RpclTiledOffsets.j2k -n 2 -p RPCL -t 16,12 -d 3,5 -T 1,2 -c [8,8],[8,8],[4,4]",
            "/0//UQAvAAAAAAAoAAAAIgAAAAMAAAAFAAAAEAAAAAwAAAABAAAAAgADBwEBBwEBBwEB/1IADgECAAEBAQQEAAEzM/9cAAdAQEhIUP9kACUAAUNyZWF0ZWQg" +
            "YnkgT3BlbkpQRUcgdmVyc2lvbiAyLjUuNP+QAAoAAAAAAeUAAf+Tz7RQEhbTKRkFYe8/eA2sR6FqxphdHV3H1CoULGed5bPvIsaGthkiJ3Cp8DT/EF/H1CoR" +
            "HhtK9X5VYrT0OGVN50SSu82Mw6/H1AgR3i4jx9QIDqzwwMfUChFCTsR/wPkBwPkCQHyBAAcrfwes9z8E49S7wfODg+cHB9QKCxRdD7pBEWViIbvB84SD5wkH" +
            "1AoLYCx/D7FqvxFimPZtwHwhwPkDQHyBQAXY3w4vish0OxE1Jwt/wfOEg+cRA+cOBBolnxFfTdbZdbl/EpjUWJNpn8HzhIPnEQPnDgQaJZ8RX03W2XW5fxKY" +
            "1FiTaZ/AfCEACT/B84ILTsHzggtOwHwjQPkCwD4RQAwbDfA8hQzNdpp/BYjLI6fB84mD5w0D5xAOQX+xixEfYL8R4gynEb8LKxEPB80dMcHziYPnDQPnEA5B" +
            "f7GLER9gvxHiDKcRvwsrEQ8HzR0xwHwjwPkEwD4RgAuV0of7Hy8WlZFi+3W/jF8XGsU0qLfB84qD5xcH1BQHZ7c8hq63WkFPDzNS3PWQEVlwtH8MTm4X5WUv" +
            "rMudwfOKg+cXB9QUB2e3PIaut1pBTw8zUtz1kBFZcLR/DE5uF+VlL6zLncA6EA0/wfODCymnwfODCymn/5AACgABAAACCAAB/5PH1CwuMzJAes1NtDeYZj01" +
            "3yZdrg4fyRtNx9QuFAVl89vZoGPT9HsVRmAE0Rpoan+FCD/H1C4RT5z2u0rWrtuknI8G/3/8cnNqf6H/P8fUCAlFrPjH1AgT8z0Tx9QKDBxAA9XA+QHA+QJA" +
            "fIGABo+/BlEOzwwj6Ugx38HzhIHyBYHzhgOFQR8CxhaY3wnBoBlMf8HzhIHyBYHzhgOFQR8CxhaY3wnBoBlMf8B8IcD5A0B8gYANLL8SizAd7H8GAiARY3/B" +
            "84OD5w8H1A4L8zUIG/DWzrHvFudx0Pden8D5AkHzh4PqCABpWm8Iwn2TzcfjFukqe8stLH/AfCEABn/B84MG6j/B84MG6j/A+QRA+QNAPhHADDzodjUHlrcM" +
            "vGybbO8NKORoWio/wfOLg+cPB9QUDMV1l8uWfusgtE8ap5EUZhxnBHNlgIJnwwv6v8Hzi4PnDwfUFAzFdZfLln7rILRPGqeRFGYcZwRzZYCCZ8ML+r/AfCPA" +
            "+QTAPhIAA6RWwoG1KxjhlWNpwMxyLwq7g4SMCqB/wfOLg+cVB9QWC31u4k6n/zYSSn8NiZBGCZjb8xwdHRL3wbqrqztv+bnB84uD5xUH1BYLfW7iTqf/NhJK" +
            "fw2JkEYJmNvzHB0dEvfBuqurO2/5ucAQgAPA+QGAC6fXwPkBgAun1/+QAAoAAgAAAO0AAf+Tz7QoHtiaBoNvN/Z/KsfUFhP+5ns+A1KLoMxLx9QWEVM3sg/q" +
            "vchFaH/A+QLA+QLAfIGADwfp/38HuNJr7w4DWr8HP8B8IcHzhYHzhwzWuwL0722UBaxkUw8BV8B8IcHzhYHzhwzWuwL0722UBaxkUw8BV8B8JMD5A0A+EcAM" +
            "MKl9eGG8/38S/L0m0isCsZedcjTfwfOKg+cRA+cWA8X3ouNB8tdEjwLcg+KWR+vfC1YfO95liPD35D/B84qD5xED5xYDxfei40Hy10SPAtyD4pZH698LVh87" +
            "3mWI8PfkP/+QAAoAAwAAArQAAf+Tw+cKCFC+cIPH1A4K8B9U6b8/x9QMCRWMQEHvwPhBAcfUBAFfw+cECH/H1DApcwE3QaEJ/NK/HhfqbtnV6cZxSDO3NTfH" +
            "1DIULF4MdyXfZCeK9h42HQIBX7d4HSW5iHn9x9QwEVAYf68onS2qsRTlfLL5LmNkDpLcxDz+x9QKCUcdVz/H1AwTfnpIzu/H1AwRfOoqAzfA+QHAEMA+EMAH" +
            "NKcICyg/wPkBwfOCgfODAAJPCpcILGXA+QHB84KB84MAAk8KlwgsZcB8IUB8IcA+EMAGmQOofwwUf8HzhIHyBIHzhABpGB8ND7EPBCHhT8HzhIHyBIHzhABp" +
            "GB8ND7EPBCHhT8D5AQAEP8B8IIABwHwggAHAfCRA+QJAPhGAC/6s5AgG9j8U2qWdERZ2aIgewfOLg+cNA+cWFCn1JxFOss7viK0HISbFDqgV3/Hcqs4aetv0" +
            "v8Hzi4PnDQPnFhQp9ScRTrLO74itByEmxQ6oFd/x3KrOGnrb9L/AfCTAfCTAfILAFFgcly3n3wRvDuEbjmGRjlVfGxcWKRVcomB4r1/B84+D5x0H1CAc37m4" +
            "UVCA2XYViVV4ej8RhKYQkMweQLt5t/Q7fxin1mZbGwxSmc0KwXnSQj/B84+D5x0H1CAc37m4UVCA2XYViVV4ej8RhKYQkMweQLt5t/Q7fxin1mZbGwxSmc0K" +
            "wXnSQj/AfCGADs4HwPkCABSlqQ/A+QIAFKWpD8B8IkA6FAPhDAVq/38BfwCBv8Hzg4HyAoPqBAdJHwW2DjwmHsHzg4HyAoPqBAdJHwW2DjwmHsA6FA+QHAPh" +
            "DAMODSmYCBMrwfOEg+cLB9QIDiNRPxAcmpo/Cy5Ms8HzhIPnCwfUCA4jUT8QHJqaPwsuTLPA+QEABD/AfCCABcB8IIAF/5AACgAEAAADBgAB/5PH1A4RP85y" +
            "UWK/x9QQCtIToMMFMb/H1A4FgZIkxg5lx9QEAX/H1AQDP8fUBAc/z7R4JF4y1lpSENDouEpC70ykl6DzYWJX41kK1Qf/MdA334EIUnwBcF1T+HhV7AFEmH8A" +
            "h1HStD8eePvY9XPmfGlw1apvx9Q2EU5Z8YqGaMRsxgz+NjHxcUl8lK7YMAZl61Tfz7QYE8xQBN6d/xhAAdT5Fyu4tX/H1AoRbvNB58B8IcB8IUA+EMAIewYL" +
            "DgWt38HzhIPnBwPnCgstsqINb9MK7lERb8HzhIPnBwPnCgstsqINb9MK7lERb8B8IcB8IkA+EMAKez8MArZ/BY1PwfOFg+cLA+cID/GgW38MPCKVPwqzMBPB" +
            "84WD5wsD5wgP8aBbfww8IpU/CrMwE8A6CAHAEIADwBCAA8B8JMB8I8A+EgARmDjxJ7PtLcAZU+uaZmifC6oqsTMmysjB846D5xUD5xoQIIcX8INg4rtj8hDs" +
            "fwZMIqqRo/3K9fYWPS2g0SfPvCE7FKBvwfOOg+cVA+caECCHF/CDYOK7Y/IQ7H8GTCKqkaP9yvX2Fj0toNEnz7whOxSgb8PqDIfUGwfUHBp5KfjOcVNVwqeJ" +
            "fx771KuOLtPpHolAHn8eL0C3kE/tQlWq7vCav8/ARn4CMfgIgBp5KewEijUUGgylLuCyT2nEHwjYCm/JLiCRt02cGXJYZT8fINEy3q2O8PgjNjdxIIlf48Hz" +
            "joPnHQfUHhF6vvOd3AA1y6c7eyzbFpyDAoSgMIt6P4YA38sbyM7zu3tEdUL54JgrbcvA+QIACeTXgcPqBQk1PiZ/wfOFDs/GNF/AOhwPkBwD4QwL/n8Ed5cC" +
            "08fB84SB8gOD6gQQHx9qCJQnDTOa/cHzhIHyA4PqBBAfH2oIlCcNM5r9w+oEgPhDgHwiAAwXQKcBXNUFZh+/z8AWD5wkH1AoDEOnXcAG1w1/D4ic8B/A+QJB" +
            "84SD6gULXkEjBtcNfw+InPAfwHwggAHB84IEP8HzggQ//5AACgAFAAABgwAB/5PPtBAK3sq/x9QIAk/ef8fUCAhyDn/PtDgTkYBfKj13Gn0+cla3A9+AiAAe" +
            "4GPTyuek7H+1Hlte5Khnx9QcEXIA016/Dk6Lp88BodPB84SB8gOB84QNR6aDC2ZcDGgqzMfaCw+oEj8AYAx39VkfC2nVjw/yvGs3n8HzhIHyA4D5AgABeCXP" +
            "Cyr1BX/JU8PqDofUFx+AgByMqAeV+CFKmWkVaYfTF/9y8kZQiy4mIB8HZs0HMI5+ax1FwEgLoBGfz8BKfgHz8xQcjKgGoxzaAHWG6PYn4epOUykX/3KlVv8m" +
            "TVtWrb67QX8HOnSq5CmWCJ1MXhRPH72ctbUxYcHzjoHyCoPqDg7vRzzCTas+7dMQF+tXEtdGn+N0iWJCfxF7HjtNBFf6D/zBZoObwfOFn4AsPtBQC/AqTLcM" +
            "Hw2Kvw8y8MI1w+oGvzBY/AGACu7AWS5/CyzqHo8QM2eRIhfD6gW/MFj8AYAK7kYTvwss6h6PEDNnkSIX/5AACgAGAAACAgAB/5PPtEQgqUq8N3kLam4osxFl" +
            "ieIHf8fUIAIzv/My9lCb9LL8fc0fd6/H1CIL0DDIMF7Ae4m1ckoZ9sQ3f8+0FAGtuP9/z7QQDcNew8fUCAvZaK/PtCAK8P8bbmZLv9+AQBRjq6sREU5zx9QM" +
            "CRqUsd1Lx9QEAs/fgBAII8fUAgXA+QRAfCJAfIHABYzHG4SAG78K+MozDFBddL8yn8HziIPnDQfUEgaI1rvZ2U9/D2DfPEW/B3Jj/3SKPq9/wfOIg+cNA+cQ" +
            "DQviWfPGUa8PmDWYUPcGdqE47PrNf8PqCofUFQ+0MBsuo6ShCBiZzGsgUldqUDR7RGNvGa3TtVg1vJ5Cyf9/z8AyfgGz8w4bLo4ToasMfwHb0WIa5T0/u2+E" +
            "B9cgTIJfGbsPfTArCR9FboAlS9vB84qD5xUD5xQLABJsk2F9fSnPGROmNe426QY/1w5HpG0aRLtb6KfB84QLJ3tvx9oIC18ol8HzgwL50sB8IcHzgoHzgwVr" +
            "fwtZDSofw+oDgfICh9oIDXLvAhsMlMAvwPkBwfODg+oDCbsPC70fC+4LwPkCQ+oFg+oDCvylfwWVfZo7CaDXw+oFn4A0fgCgCv32s18FmVcBT38JoPMcesD5" +
            "AkHzhYHzgw7bFl8Ly3ytfw1MQ8B8IIADwHwggAHAfCCAAf+QAAoABwAAAmgAAf+Tz7RMAYJQVO3bs1GcbwJpCVnTvk/Vn9+AwA4xMpnWJVc07P34ICjmDc0W" +
            "Ndn99kh3v8fUJgkQe0d3pCIxWE7VB0XThKFMpd/PtBALDpzx34AgCBy4dd+AKA1n6Yl/x9QQAkwMnX5p8G/fgFAI+s4piyMVq0B/34BIESWT5Nepe8CTx9QE" +
            "CD/PtAgEr9+AEABtw+oMh9QTD7Q0EeEbKZXTU7gu02tvC4LSN4n8Y6c/DEsMZa4xtThghAkur8/APn4Bc/MQEea14bjvdBgBbf49h7d/C5FENZTMZIOE2u8M" +
            "SvjqnOLh+9plXrKEDE4/wfOKg+cRB9QYAwmSo7oSkV4zIRbwyt06sgx/Dn1w5l5XkvR79SFfw+oMn4BsPtDgA9O8oxJm80ue8b7XGzRB3w8HticIA5tr3xkD" +
            "uNmj6r5br8k0PP9/z8A2/MOj8A4EL8kcD2ukBUmxmCjZGdgmWhIiK3LXdmghbg8YK+g3x9w66NnS0J4AH8faG35h0fgGgBnABOXG9P44Gn+XX+8Z2CaPj5e2" +
            "IRalQ7pEJxgr6Qi6ghmuXpNQhkfD6gQKQq+Hx9oIC6GGX8faCAuhhl/B84SD5wkH1AoPkt5/DWb8vw9RxIc/wfOFh9QJH4AwCu7S2H8N0qyXEHxxOw4twfOF" +
            "h9QJH4AwCu7S2H8N0qyXEHxxOw4tx9oLH2gsPtBQAzsY1K8FiMt4JQ0xAtKrz8AWfgDQ+0FABess5zAC6jFAjicIs35Tqs/AFn4A0PtBQAXrLOcwAuoxQI4n" +
            "CLN+U6rA+QCABMHzggQ/wfOCBD//kAAKAAgAAAEZAAH/k8+0KA5DtidcKfaVByrfgFgMCvpyv9oJuIt8H9+AUA11PYuJZdtM14HH1AgJIFB/x9QGAlWi34Ao" +
            "AZaX+n/PwDYfUFQ+0NAD7smgXSntxNPMMcH2DAsQbNkpfXqH3wF/c002TEEd8I5+IzLfmHx9oVH4BoAGntGgrWX8ZTTx0gm3+Y8LJAwxePsjMwFPCGpAMj5y" +
            "H3kXKqdy5d+YdH2hcfgHgAae0aCtZVJTmUJXByvHCyLvoPVHwwvFr38IaHhFTF1qA3GhRPWDtL/D6gWH1AkH1AoMzotufwv6MF8Ksueah8HzhIPnBwPnBhBt" +
            "ka8GbocOJn/PwBZ+ALPzBQzLPz5bDCmXtb8QaoA/yf/Z"),
        new("Poc", 33, 27, 3, 8, false, "opj_compress -i Poc.ppm -o Poc.j2k -n 3 -r 10,3,1 -POC T0=0,0,3,2,3,LRCP/T0=0,0,3,4,3,RPCL",
            "/0//UQAvAAAAAAAhAAAAGwAAAAAAAAAAAAAAIQAAABsAAAAAAAAAAAADBwEBBwEBBwEB/1IADAAAAAMBAgQEAAH/XAAKQEBISFBISFD/ZAAlAAFDcmVhdGVk" +
            "IGJ5IE9wZW5KUEVHIHZlcnNpb24gMi41LjT/kAAKAAAAAAjUAAH/k8+BcBJegFsDFy8B/H9uMEWoxJ+19RoEr+5d/lJAhnv+MInzlrqHgvzXKuYCkjaS3xDA" +
            "j6rFASZ7srepF2NIxwcdFjwgi68kjOWYKHSH3NHozhZ0gIP15kt+gzrT9lEmX3DODnCAizt9iM2IBqJQjakKDOvKiVE6cJj0Yy64450tjZ433tgsvECUA6+x" +
            "V5a3W+p/F5rrvoD8RIAp1UVTbylgyPhZNVpUyOsDdTf8BoBmcUmwFiB1ySQBtHse9MASaYburNyGxbLIMIH8JPwmfAMAGFEbkmuArCtyDGOKMCRHstjptYaZ" +
            "Xp0z/OC2/EO+IxHyFn7kojG7Cjfoh8sA2H0ZNAmrMYqwVrhxb/sKmJwma6HVzM+lnBq6hRTxVt7G4HmqAiq59JsZItsmUUCcIPxC/jKx8hLQ2dz4SRHY+Gg7" +
            "Cz9x4RXnczr5tpkx4fINVwmPL9HwjIVQ9kJtSOYjHMeqhxF2qpG+58fGg+IxHynwnIe1S/XW13c/xWvmO3OAq+YlB7G4r1wgrAW9ybK0Buk7sOBOXjw+6dGp" +
            "rFjhtH8lVZ1Vio2DtBOcpFMjW5yyLm2uHmpEGjkcyAqSmYQxcE8QCpZqMRGT/GZ/GZfDWh5HiQ3OsoAp900ftErdalI1FgVpJ4/0i0sMf2lHE0/GwIC+zRhJ" +
            "vTRulu0RhBrI99yNZb+rkB73Kjka+I3fVytgu79e1781Na/TFYDPuvB2fulJw3X7Pr0Wq/LVM2cBRkWoAkSnlyMA562ATZnIo7WWiOdtSBoslSLvwYVitKnt" +
            "5y5VYGVRPRnCtdbHDQ6wZ4Z7z5Fs+RVHyOycnTWKwVYGht9zp0QXcQoUEgkowzjy2TAGb5LDsEBCaamBwe/YVitSa9H2gYyft2WWlGzkgUKHguqkH8UVdgGM" +
            "g0JU0TZ0GG41ptaVqzYiid6O17vG5vyc1xjRbKYDtsknBurBR+C2Prsg69agXgLhGffRcZlDs47UOI6Vl1oNUScmZ87Y+MyJx7MCH2HniUQO8vkAC608YeHw" +
            "XSQDROpYBYHDQfxGQMpOcKhxF4jySvxIPkCWkjfXGcSV841dvz/8hgCTHuqRFYQMRBRYWUrWF0lrEhquXhOVNb/8Zr4jXyngDiALEZ3Jyc8MyP7Bk2m57EsM" +
            "gL1fkIECSLeGahgQyLu1lRQEtEy6MVsaIouOvjvD+JG1F15C8FjZjmSoWu+mHW79St0WCXBFXrSTEO/tRyzLSfyn/kNfMgBDZLewaMNElBAC2J9xl57Bx7lX" +
            "DAlEkqfKvoDNEpub72COCstLZRkbujkucg9xGX59zzY6+ilzKVOYjB70GOT402Dt8AF9OU1GjSAWzSNgBB4xR5MLP+VgRfyn/kOfMhBo3QGXw1CTmhEftt5L" +
            "N0igTvFPLCeN0/lMaI+RjRLf14xq5bTZfvF+mzWtkGw9x2wgmE850UL65fWC43POPFoDvDXA6M7uQvfvmhL+deD6jlJOM9VfCnySYtmej/yW35Lb8tmA4YDR" +
            "sJmcDw3qOqZYncr6dr0lozsLgDs56oshbLcbUbojYrWZLThLGe514kZK9J/rCQ28lrShMlE5SbCgmbNvV4OzNoFyV/i/Tc2FYJ+KUzqmtBimsxGLF+RbRvep" +
            "PjH6h1TvqJzoQ9C3lQQaMOpkho8VHuucLD1wOhpNXkEBDitHLRqOa0ZEquCwFCxaPwmENkFieGkf41Laj920RPJYl/hLd0lCiC4VUxL+/yBbKigErU1de8hu" +
            "eKsbxJHYpv7XajN6KNL8us5JiE3v28HeLxwfH2JX/Bu/qF1gu2Qlwo3ByQ574FcD0KILpZP+p/e6vL7TOzxbcCkFoiRKa0ukbWRc+ipIa+FOPHwFGeM5Oajn" +
            "Vc9OmyXxKPqmRugjMaucXc+oVeGpcK25HQljfbzhORArx+c9TPCCU9/mcE5yOGX81DfNQX0UMJCSFW+UxIv22UqiwWgZtKJI1Oc+Ryt2b9Q+4yo6TsU3hDCX" +
            "xAglMn1RGVS1s5z2mbse6ckObNFiALmWVHZ9/FjfC1v9I1D/KLpNRB2W2pd6/CqaLH+LAx/WZmsqed6T27xJGtrkMckGXkL9QYgoAXfuEbNoekn0O52LkKEi" +
            "Ju/HjxqYTYMvcAfiinqy9onZenrqRWfExgUjhw3wNOhBp3iVKGiL4cwTCK/y0IMep3H7SROIEnESofNr5K1/33CQgtnzYR1I5XsiDLk/xLUGLvIB4rM2X7dv" +
            "gTTh+O+3nGydypLBhKFUuaVRWwmQxi1QGlfd/r6YCpKDMbqktIR4a7QUEaAX6hbkTdjBfgW0I01ytHCJnkigZIzzj5ep2o+5qeYes7CnBBQHNhaOPrJSADKh" +
            "C0kzTJXhRbfjt/dthz3EgJrvJhusNejtTuT9QchfA6Sv8aYjn1pCH1wvtw+9uNE0hjDAz4RTYDuyJWP4Kaw2q/w7YKaEVcjQEVUFuVJKviFXfMISPklpKWyx" +
            "lu/n/Noj5tDfNcyLQ9kSs+Aoss45B7rg439gUSSHTIwSP++zydIHMT0mjAbSOJ0xHgCp4VNrRoNV9ahzW1T3adDptmaT9CBMsqOz6M+V8O2K/tULmS2vOQ1x" +
            "n9PP/ukoWfmkrdacffxSh1xx1YKSRqbPSw0tPV6we0fHcKExmFQ7JUY1/dBZycokjqGgBuKURrl/Ntb8rxWr5rwO7KcUUbJENBw9uf92xtagOwrxGs6SqUlC" +
            "WECdR+/lSAD301vk11Laawce9h3KNKp5Gta31urgXtSh4wteOffmgHXz/BYzFG5/tbciZV9/n3vzyRuiUvVnNpB1bzuUhOO2kuuBBGO/sPzs/3VJNZjdUi7P" +
            "85bc+GW9r9d/EX9tVtP786dgu0mUxEf3CgSGIPjsOW+IhOJPSY+HaiKBScMYelrefBKG+htmgIiVJgFO5P1Mqpc9B+7AOO1Uw4OgoMJhMZ/Hg1N4I3NXgxcN" +
            "3W9m2snzyoA707aaMasMuhFVBblSSr4hV3zCEj4CJFAHHbp8/9k="),
        new("Roi", 29, 23, 1, 8, false, "opj_compress -i Roi.pgm -o Roi.j2k -n 2 -ROI c=0,U=4",
            "/0//UQApAAAAAAAdAAAAFwAAAAAAAAAAAAAAHQAAABcAAAAAAAAAAAABBwEB/1IADAAAAAEAAQQEAAH/XAAHQEBISFD/XgAFAAAE/2QAJQABQ3JlYXRlZCBi" +
            "eSBPcGVuSlBFRyB2ZXJzaW9uIDIuNS40/5AACgAAAAABzQAB/5PfhR4SNfAkL+ZAzTIIHSGf3NQUrtWVA2uCtg/2VAhHxvetsR+FcOatNiQdMK2tdxXYI9NX" +
            "QHeXeKKCbQ9qPcT07v6Q6KF987Sl6MRnAOxneDO0TXXkNFoClT5/1aSULACC02jkLBk0x9CZJIvTlLq9HXUh9kKyZeSzWVniaJjEhpXHKDlknWnCLxQp6dy1" +
            "cEBe6sD5LLB89lgfPWQMhxc8Dve0lboJ8WYUGgrwBXmLNL29SzJ7MyztQjt011ZJ/gvsqzMohjWp1DzAtVdi33zzQL3TSP5dweSbiMmkG7caBVwf1th0ulXq" +
            "CNh8uMwEJlobq3ANxcBw9rdw3Mp82+j1dmCCbDNHKwTay08nwwLkUAw4OrMOWsAiWs5wO1f1U31prEhwpU5ASkvBXkaRS6xqANRbcYnVMDJ7VGyUDncDT07E" +
            "xq7zGE2dwxpGpZFES0cOEkeIH9oNlFqQmIXyhYobMiyADTxvHNIWH/CozPA0OjJkPdwZ5juMGYQ1nt/GdDXXUv8gsfrh6mk8Fm9VchF7pJfIFKF/6f41qOna" +
            "i77n2XZC97eTC1JS0fJRfApy45A6QtJ/K2xv2waZg9OCm2f/2Q=="),
        new("Bypass", 31, 26, 1, 8, false, "opj_compress -i Bypass.pgm -o Bypass.j2k -n 2 -M 1",
            "/0//UQApAAAAAAAfAAAAGgAAAAAAAAAAAAAAHwAAABoAAAAAAAAAAAABBwEB/1IADAAAAAEAAQQEAQH/XAAHQEBISFD/ZAAlAAFDcmVhdGVkIGJ5IE9wZW5K" +
            "UEVHIHZlcnNpb24gMi41LjT/kAAKAAAAAAJEAAH/k9+E18XC2WyAEjcfJBfzIGZXHnk/Xt698ZeckCwElZrZI9PKwx4h/TpUkxVue1q4qBeDEi6aV5bA0OIN" +
            "sO/9m3hoiVDRaLkmfwq0b/KjRtBxMeRUEKJM5ShMLSr/f4LV5+Ymi5Sn1p1EiAIQM534HnXg5uqSQUEJKgL/fzHayuH73KfpHpDo0p1hLRPiBW+zOWPySVA1" +
            "Uf9/BmpCQIURwb9k1XI2+jxtle6Fie+Cf2kJHbMq/3/A+S6wfPZuFA+ew5IMhxc8Dve0lboJ4UxhSHXYUgclTkf8JgUXt7vogD6ieL8vsScOyFCZ/UhSJkHv" +
            "iTua18KCH35gWgv10QPInWiz/10mRiVfXXjw3r+0QTjA+sr0CfGvC4I4kcJ6xhSkAnpugHGjqUYAvROoG/l8ybjTV6gacS9loqpcpX7UX11Vy/Y/EAFrv5+v" +
            "vyUFNHzK4t28b8eYIOjjDf16LoOq4TNlsuEPrjQYjoZcBgG9y0IGt8JlD/LzqXF4XBSX38TVskA8prIBHYJPGtxHv6mOEgcvrtrbvJMgyb2Fzn8PGx/h4YKz" +
            "xs6gMwbEW2dLRtQaTrjGfUURTgBV/38c0hYf8KjMujt30lChUjwSiSJyflz4qF7aG+2Ho7dTubsn9OeDVvgaHvpzT5hoaQqdhNDtRJVoqKeopApVL/ERI4fV" +
            "h2XVUw1728jlmGREL7kQNuMcuwvq3nePxorwhC0RAAAAAACiAIQBKQgEAAAgEIAVYAAgCLA1Cv9//9k="),
        new("TerminateAll", 31, 26, 1, 8, false, "opj_compress -i TerminateAll.pgm -o TerminateAll.j2k -n 2 -M 4",
            "/0//UQApAAAAAAAfAAAAGgAAAAAAAAAAAAAAHwAAABoAAAAAAAAAAAABBwEB/1IADAAAAAEAAQQEBAH/XAAHQEBISFD/ZAAlAAFDcmVhdGVkIGJ5IE9wZW5K" +
            "UEVHIHZlcnNpb24gMi41LjT/kAAKAAAAAAJxAAH/k9+GEYJEkIl4z1EW4h6ELQgSNz/EF/MgX3+t/S658EQ1OlxzkgWBAiGtj0BVgz99wIF85kwbRT+FatFJ" +
            "xSrqSW1xXlrf8heA37drpqhc/S/Lbxl7YG0m4AabInCe2AOKafAfyxj3/38AaKR0eCweeDO0TXXt+ucWlU5/nzN4rfa+0PGb/3+nPJ9dreTG3QDJJKyYfids" +
            "oVRZeAZv58bnaSj6V/9/tL904kFBpXHKKMzrcxAaBvytT3rqfLJNdzGmsP9/wPk6RUkI80IZMKD58Qof2SGThGsKB8+lIKdpERwhuEAMhxc8Dve0lboJ4Uxh" +
            "SHXYUgclTkf8JgUXt7vogD6ieL8vsR9+UoUJn9SFImQe+JO5rXwoIffmBaBMoiB5E60Wf/f/fz5YMSr668eG9f2iCcYH1Z9QJ8a8LgjiRwnrGFP/f3YAem6A" +
            "f3cvxN1mj4CqDhh63oeuDHEaYmf/f2Wiqlusq+WfcX8WNW1a26leHM9WMC2nLtczJXOYWuHJabEVUKZWuDNLmAyRmbuyDcOiewiLwt85HtwMrzTZ24uBkvnM" +
            "N/X/fx87D64We8ThFlMZ9Rj0BjSap98BRpzeu2tu8kyDJvYXOf9/uhxPSB4POYg6qi4P3bnvrPrAyYAR8LVugrGwOf9/HNIWH/CozLo7d9JQoVI8Eokfmavy" +
            "58VC9tDfbD0dup3N2T9/nxy3VvgaHvpzTyfkNIVOwmh2okq0VFPUUgUql/iIkcPqw7LqqwGve3kcswyIh/9/FHIG19eXmF9W87x+NFeEIWiP/3+h/3+FeTtg" +
            "zMRfTi5FyhMFo/9//9k="),
        new("VerticallyCausal", 31, 26, 1, 8, false, "opj_compress -i VerticallyCausal.pgm -o VerticallyCausal.j2k -n 2 -M 8",
            "/0//UQApAAAAAAAfAAAAGgAAAAAAAAAAAAAAHwAAABoAAAAAAAAAAAABBwEB/1IADAAAAAEAAQQECAH/XAAHQEBISFD/ZAAlAAFDcmVhdGVkIGJ5IE9wZW5K" +
            "UEVHIHZlcnNpb24gMi41LjT/kAAKAAAAAAIrAAH/k9+FThI3HyQX8yBmVx55P17evfGXnJCb1R62Q6lzlvuhTuE65mvTI8xarioF4MSLppXlsDQ4fT/HxAFu" +
            "h+J/hawpvfZmbtmI0VWlXvBxTT4D+WMfDnbBA5op8oIjQ8Gdomuvb9c4tKpz/PmbxW+19oeM3rSLP3PAtt0AySSsmH4nbKFUWXgGb+fG52ko+lYIrFlMUGlc" +
            "coozOtzEBoG/K1Peup8sk13Maaw/wPkusHz6BgfPcwyHFzwO97SVugnhTGFIddhSByVOR/wmBRe3u+iAPqJ4vy+xJ+EaRXg4NtMLKYV2rY+2Ru1uto1eOy6I" +
            "HkTrRZ/9zTU6wZOtV04JKYm6XduLfoJ8a8LgjiRwnrGFEfzAUHOtKXVKMAXonUDfy+ZNxpq9QNOJf2WiqlylfvuUDyOsKAoqEPCe1Ah66nFr48h51COLUIj8" +
            "N0AcletygpCCVa6SpHeWmIiPIUvcTwBglJhVVtGMTwpcXhcFKF9VMjD+xM2EKQHmXy90EYPVMcJA5fXbW3eSZBk3sLnU73ON6Uor8COqouD92576z6wMmAEf" +
            "C1boKxsDnxzSFh/wqMy6O3fSUKFSPBKJInJ+XPioXsoTmk1+WEfP9cKJlry4oSrDoxjI9PfjOSwqKDxzsf1DsVy+DSeLOfZsSWFIDSJ/jTaUNe9vI5ZhkRDv" +
            "A2wS/2uruB1SRv5sdS+oZdvE37O2DMxF9OLkXKEwWj//2Q=="),
        new("AllStyles", 31, 26, 3, 8, false, "opj_compress -i AllStyles.ppm -o AllStyles.j2k -n 2 -M 63 -r 8,2,1",
            "/0//UQAvAAAAAAAfAAAAGgAAAAAAAAAAAAAAHwAAABoAAAAAAAAAAAADBwEBBwEBBwEB/1IADAAAAAMBAQQEPwH/XAAHQEBISFD/ZAAlAAFDcmVhdGVkIGJ5" +
            "IE9wZW5KUEVHIHZlcnNpb24gMi41LjT/kAAKAAAAAAiqAAH/k89dhPAQ4l2zuS2wVQUoMuyiQMmzTvP7k8KV8FTk6pEYQkgifWqgIs+W3kGG9zA73w5BBjKS" +
            "oJcxGUtvFYebRzG5cMtHq4qIEzjEUyeyQ7U6pRN9eMLS4mAC662NsouB9J+tk7H9rd8WEEIghyLBbtesvhGsaxEd69a2CQkXCPtmbNL84H6daWwjlAL7WDW8" +
            "HaMRAL9YAMlukT5jYJbTbCsdsx40AJUSyNSPK8+R5gB/2aWRELt5/DPGioSYS9nbxVGsY68WanfiDRgNEwuRt1iLohZAXeW70SxlXM2uI+Ewr2paNfLvcVBY" +
            "17s1ynRZIskQAgSRW4CyKwiICwKvnrGwir/EyF2eavhJhDrBcDCAO77rA3b7R/osH3/PMguSMeefDLbiDBrx9UiCL7Sjxmq9Zi3KB3Hv4NU63KXMGBL1IINA" +
            "xTR3JLRqXhBX7zE+y1fQiOJOtvPlDgq3fVBLXDmVQpnSNeiOEB8r1Rh9MnVEw+TGEEpREITvy4KRiGUaVknD5cYgSlGEpGWAlbX17ceKnGaJkeFD61Kl3f6e" +
            "4Gb5iVyyH1FNJ0A9URNhF/eQDZc3+mBRO4h811ddUyVW8GPJ/uhiWLDbmVqslRLI0yg19j8/qrUiFu2KgXSrHwdUphgLaBlXe2VB+Ekw8wAgAgDwARkBshUV" +
            "AKviwAFYcijDW/GqU96hgApSsna9a++R33u2fFqPIjeMTIllHMq6ks77hB14p/y/RHelkyIfnzAYFShOqoxaWHT6XVDjJQ6AUHm05E9i22IIGz3wD2CIFnT9" +
            "RfzYQSlEMg6ZOP5sQJCCGUmspH8uCcgiEIiJYj+ga5HdPvZBg+ggvl41o4hMX6lDi5R0XokMj6lMcAgmx9OHOe6WQ0AkNlA278zukK3DfxjUhfbiFWuBI7hd" +
            "LAiWGmQbjcIkAAEkejykyIKNqxtXGR7SOCSyHTwh7CQOioI9Y0EGAsqMwBCqEdUZPbEqQq5Dqwv6Pv9z/wtnvBm/21szjtsue4gz1KAYv00gC3VFImXZ1laf" +
            "77dQ4WbCAAAAD2GiBIlI8XkvAOA6Mw09Pv5MQE3CILyJYvkXSF8Nc7PWbBg5QumSDqviJiH0ja+3OiK8aJE8W4AySOIlGDsP4VUOPGw35tcDtyKpJFUKDXSq" +
            "1MfJhBCEIdcISfLhhCQIQiDzvx4IRhCmYqC9J6w0wwDXDI+pTHAIJsfThznulkM/uRdehoqJCIGiv56E3fiRSp2M1HpkjMYuK6CrCOl2AVc5KgQ6bNbzPH7Z" +
            "jPBq1Q6/lDIrNJplTYqkxvBtr3cWIE9+qPPg99M/LDI0vNDUiuaANE/U/Gi1+tdXy57iDPUpAxfppAFuqKRMux1itP8+31SNwdS2yAWsAJhc00sVOULpkeQ0" +
            "tXeEXKOfgPyGpop3+QUt30j49GYc9G0sDjxsN+bXA7ciqSRVChg2Xc8VvTT8IIshDoK2+kFCQ+YUXTgUm3LmZ+owR5KpHzizL1nA5fB4tvTtl7kcIzCapqv2" +
            "xb8UtRRXahqH+rZqlBuiVbb8gIdBDoIdBLYqrDd/BbyJP1QAeAR/xg8AaXTe7APZdZBQT9i2KnOZBfVBcybLEgUOIUkMw0NxU39I4GTxhmuQtpViMxEJB3u3" +
            "vQdVSFOXh+sJbE/k+yr970o9CLb8gINhBoINBLYXyID6Q35Aq/9D/cAc+H9DTZCJ9gLaJkDbpyq2QGb6Cr6M2TTt+vHetvM8vJ6sgLcfq3kzFqq2Eczu9viA" +
            "iEL4qresaHgU9oOwGwTVAhC1AXO2/AqsS5h+BlQXxD8QyJtCWoQipiCtXOVceKGrhPOJp3krvaQw1UWYEAA1kObICD6Dxa9eUv1T1aKf4/pTh0rodNOCIqA4" +
            "CgoUhHY3YIVJItiqtqIZT4a5CZiixPJFEdVWZErIjPkXYA0zgWpIKAiBlLi0EoMqNWfs9FukHem/wx+q+dx34gN3Yxvp0EAgUo916TgnDkZRtkCCykkgFQOJ" +
            "9/UMtXFa+V31Gl91986qVCzUpGkL8mh1oXp2vreq/a0R0bcWyB+2CAIAAQpqXJ5z2lNl1OvZeotCc+UtKu9KpAq2/GdAJ6hHcPxnSCa4R5D8x7gqkElQmqFB" +
            "euTo3c24E09bacXaOSZyS9NB4ZDyLAJ3Y7wvGdW2Gd/5ql+fZTwoC+sClqTbTZrRbG8fdWnRYBpMirYK/3Lsx7fuf/xdOMTKuo/AXPZlG3EkUe/FrLZUw87a" +
            "V3z/V2/wFXb9AqlMwGPHuInIG3UhhpyTfetjgrbJHeX6ZSop5rGbLB3xB4AEciXLWVw1pVxyA6LjZbbelRUSPrJCwDKNIU0q1y16BCNpAqfG8rYuaYPitghY" +
            "3AAdCjVnVhYYBZju1VoBAIBCCkYJAQWtqLjStpu4pAi6iRM6MxpheJIKLS2Y1FVq7w4liupnWF62AAAACoCPg7p+XnVuubabTb6xV91jTjJcGLZVUVEhRJV0" +
            "fvrD0beox8Ef0t+SP/w9C9qHX122/Gg8KKBHcPyjjfEJrhHkPzINCuIS1CeoQCC9cnG7m3Amj1tpwvu1Q7xM5JeTQeGQ8iwM7omNgqq2DO/81Tvz6bX4UBfW" +
            "BS1Jtps1oti8fdVOhEnlUbYFf/TZj2/c/Pi6cYmVdR+AuazKNuJIo94WsrYAAAAPAgBF8XkvVMPPaV3z/rt/gxV0d1AsCUzAYePcROQNUUMNOTzme4K2yQvL" +
            "9ERyueaxmyQd8QeABHIly1CYa0q+SISl3lW23kqgmj6yQsElGkKaVa5a9AhG4AqfG8rYuaY+KrYIWNwAHQo1Z1YiBgO0GgtaWgEAAEIKRggEBcBgEgY3cUgR" +
            "dRKIzoywGWF4kggtLZjUVWr+OJRcEa/etgAAAAAVAR8HdPy86t1zbTabfUVfcYBGF+wqtiqoqJQoqKLo/fWHo29Rj4I/pb8kf/D0LfDX7qq2/9k="),
        new("BypassTerminateAll", 31, 26, 1, 8, false, "opj_compress -i BypassTerminateAll.pgm -o BypassTerminateAll.j2k -n 2 -M 5 -r 6,2,1",
            "/0//UQApAAAAAAAfAAAAGgAAAAAAAAAAAAAAHwAAABoAAAAAAAAAAAABBwEB/1IADAAAAAMAAQQEBQH/XAAHQEBISFD/ZAAlAAFDcmVhdGVkIGJ5IE9wZW5K" +
            "UEVHIHZlcnNpb24gMi41LjT/kAAKAAAAAAJ+AAH/k96JhSQSNz/EF/MgX3+t/S658EQ1OlxzkgWBgPyRCJeNVBFuAiGtj0BVgz99wIF85kwbRT+FatFJxSrq" +
            "SW1xXlrf8heA37f9m3hoiVDRaLklZ/CrRv8VGjaDiY8ioIUSZylCYWn/f4LV5+YmRcpT606iRAEIGc78Dzrwc3VJIKCElQHAukg9iFD+0DulIKQMhxc8Dve0" +
            "lboJ4UxhSHXYUgclTkf8JgUXt7vogD6ieL8vsR9loqpbrKvln3F/FjVtWtupXhzPVjAtpy7XMyVzmFrhyWmxFVCmVrgzS5gMkZm7sg3DonsIi8LfOR7cDK80" +
            "2dsc0hYf8KjMujt30lChUjwSiR+Zq/LnxUL20N9sPR26nc3ZP3+fHLdW+Boe+nNP/CEPIg6E/38x2spXD97lP0j0h0aU6wlonxArfZnLH5JKgaqK/38GakJA" +
            "hRHBv2TVcjb6PG2V7oWJ74J/aQkdsyr/f/xlSQjzQhkwviJDJwjWF8baREcIlRB+UoUJn9SFImQe+JO5rXwoIffmBaBMoiB5E60Wf/f/fz5YMSr668eG9f2i" +
            "CcYH1Z9QJ8a8LgjiRwnrGFP/f3YAem6Af3cvxN1mj4CqDhh63oeuDHEaYmf/f4uBkvnMN/X/fx87D64We8ThFlMZ9Rj0BjSap98BRpzeu2tu8kyDJvYXOf9/" +
            "Dxsf4eGCs8bOoDMGxFtnS0bUGk64xn1FEU4AVf9/J+Q0hU7CaHaiSrRUU9RSBSqX+IiRw+rDsuqrAa97eRyzDIiH/38UcgbX15eYX1bzvH40V4QhaI//fwAA" +
            "AAEAKIAhAEpCAQAACAQgBVgACAIsDUL/f//Z"),
        new("TileParts", 37, 29, 3, 8, false, "opj_compress -i TileParts.ppm -o TileParts.j2k -n 3 -t 16,16 -TP R -p RPCL -PLT -TLM",
            "/0//UQAvAAAAAAAlAAAAHQAAAAAAAAAAAAAAEAAAABAAAAAAAAAAAAADBwEBBwEBBwEB/1IADAACAAEBAgQEAAH/XAAKQEBISFBISFD/VQBeAFAAAAAARwAA" +
            "AACiAAAAAcsBAAAASAEAAACjAQAAAdACAAAAOQIAAABeAgAAANADAAAASwMAAACdAwAAAakEAAAAVwQAAAC0BAAAAgMFAAAAPAUAAABkBQAAAL//ZAAlAAFD" +
            "cmVhdGVkIGJ5IE9wZW5KUEVHIHZlcnNpb24gMi41LjT/kAAKAAAAAABHAAP/WAAGABMPD/+Tz7RAElxH3vaQQXxhnUEcFuy+p8fUGBQAULyUCy6ysHMEf8fU" +
            "GBFQS99MwCuDQVun8/+QAAoAAAAAAKIBA/9YAAYAKzAx/5PA+QZB84qA+QWAFqNjkE4GiQGH/cJ/GdcLmitMffaoHx1Dpru+DW/DGNsfwfONg+cbA+caIlPJ" +
            "HI803I7z4+pHZw/viYjYnwsIPKjHSN8Zjw1RRZstdOc89P89wfOOg+cbA+caIlPJHI6EMvz8JjsN7H8PSVxYLoLFmdz1gaXgGY8NUUWZc4zvuJnHj/+QAAoA" +
            "AAAAAcsCA/9YAAgAdIEggR//k8D5EcD5EcB8iQBeaqI8aGmEKjpJuIy+JBKxnkQr+643FKlgbleu6R975z4ErxlXYEHsvBxwLb/pcnLSMoSQkCB1DIL2vzl3" +
            "dbLTCpyHXfbpFShOrNzNqRV40PkW0WFWATYCiM4Ksjezx1hvO32lGyJ56GPrwfOzg+djB9RmDI+pTG+wKSfCG3uNZQSZe7uNlksRkZQDReqtN8AM3epJCrps" +
            "yccU/zKcN/shnYRH2uO/EdUZPbB8vXCKy8OPJgsDg9YHSMmw+dyyLhmfD1MbGKQ5imBO3ISZ8TO36cJe8jr/fzUgxrEWp+EOKZ6chsVtzYP+ed2dVGmFyaab" +
            "VR8jrZjTsxhAYR+j1QyJ6s0FLxtT0C34JsHzsoPnYwfUZgyPqUxvsCknwhz697I3mj2TiOAmwmHBzkBFls9RU+rIthOLlC0oXU1de46Hy5r56OY3FiBPfqm6" +
            "AUe7x20oTAYX8yYRF+mFYcjP7ssyC4Qp8AU9H2BsIXUJM+LiVzz3IlwEPzUgxrEWp+EOKZ6chsVtzYP+ed2dVGmFycVCOY6fWzGnZjB4YDyKqnFnZrgGRNx3" +
            "oFvwTf+QAAoAAQAAAEgAA/9YAAYAEw8Q/5PPtEAj8yYLS5+/93YUvobxvfZfx9QYFABSEvVEQkyiWkevx9QaEVBHFnVSQ77AxKdnv/+QAAoAAQAAAKMBA/9Y" +
            "AAYAKTIy/5PA+QVA+QVA+cWAIiH+zhEjzXDQQwN71c3OU2gbQlwkx3iuJgovDFOLW8HzjYPnHQfUHBTU5BbgScgrtKoNi7sI/DafTjDkd6aE+wj47xUpol7/" +
            "J9HzgLdOtsz8wfONg+cbB9QeFNTkFxnTaU2l5gUvXwj8NpI+aQdrR9Yl4i0VKaJe/yfOGgBNeZwexR//kAAKAAEAAAHQAgP/WAAIAHCBJ4Eh/5PA+RHA+RHD" +
            "7SFiT1nHcfn5B8mVh5jxg5NOevSydejplAnIhJL4WNpBD+3dHzhWieR12eGR5a7WOtYh4RA5BV3wzePozsqM9fKVlCPZIKavYlfoMiu6m+dqJkrlJ9h9/xfV" +
            "GJLMSrHO+Q8FFbK9x9eLx9pnH2mcfgcAYlkU/Ol2JBmDTEuvQ5M1dqNDQq2cdNNp9P32XESy6El1Vzm5UGTJOg47P/cPbYvy/2DjYlh36U5TuUe84d0SzgBV" +
            "8c/9y780+LEFqXLT7FuS1RG0B+SXJRnQLdd+2+qraBxob0glYkXSkvLz3KJDT3iEwapBPzPELRew2QqFjlmBZ2eTHUGTBgOC15Ck3WrS9b7J+SLQ7U3ShYxn" +
            "XH/B87GD52UH1GoUmHUl4iVhjHdrYbpyQGD1kER4Zc3j7Nnisf57y1h1bvbOg1Tl1SCbQAThYAbMgHKFCaq77+btggX1sjZKdbgEnH6gVB3kCOVRz+00Y1SW" +
            "o22lObQzF+4E2r3IMC1SNl/fWfcV9n4UKRI4D5BD4Xp0RJCgcHuFSf0X9GyBj9GtncAALhkXTU1RElQxRebJ+SLeKsOlCxjOuP+QAAoAAgAAADkAA/9YAAYA" +
            "CwwM/5PPtCAFLOMBvsgXW9+ASBp+LeKwxfxmssfUEhF8VPKQ5VOc5/+QAAoAAgAAAF4BA/9YAAYAFhkZ/5PAOgw+oHg+oFAEAWtPmdha7QJFqfhPwPkBz8Ai" +
            "PwBQByGvAVVHoJMSfoACHGTGXMD5AcD5A0D5wwAHKZALCa9L/PcQzDSw/3//kAAKAAIAAADQAgP/WAAGADlGO/+Tw+oNh9QpB9QeIOws9tA+dq2Qdxt3tyR8" +
            "qdb4hjlpJSOAX28PpGhXYGz3HrQDrhY52pgFrsZNDz9fz8BCfgNx+AkAIOwwTeEvRPf6BLkT5rmQyyR8u8zZgbqoBi+xHLUmc2UpX/cGFtEAZr7/fxuv3K4Y" +
            "iCFhHeNSVpOxR7mJF8HzjoPnKwfUHg9Fri3bnRyeZfxbU0hfEYaAn2qtcm2OCXMG7NyQ2yOKT0QnH6VP78F2xoyL1CJ3LFQ//5AACgADAAAASwAD/1gABgAT" +
            "Ew//k8+0QANJ+g9u8udyUluQVH7kKt//GIAkzb7odh5vR9zk+trZgM8Cx9QYEVBDvZxETowaq2in/5AACgADAAAAnQED/1gABgApMyv/k8PqDoPnDwfUFh91" +
            "4dmEAh/t/C3vNISfINIWUGfcExMsGc86qfhsalJjz8BGH1BcfgHAH3XvBItBmYRXv0pCWD5wn88TLMJivlmfY+jr/iGWOUWRI4nMrIq95bd/wfONgfIJgfOM" +
            "EesLyC/s9zkvOMVovw3AMCFMOwm4PwvnESlnnq5e/iGeP/+QAAoAAwAAAakCA/9YAAgAcYEagQb/k8PqJofUPw+0jIdjPD14k1Rfr1Xe0MkKYbzS8dnEyjYc" +
            "ilaqkmoLqVKJ42RWMTQfX1pQvfNGwSrhLxkzfBVJ3AZNevksGBt5WmnPb/XBfzkT6FRnhLEZZEmF4veVl82EG26MMv0KXzaBiVu+M0IwmYBfz8DSfgXT8zCH" +
            "hs6KNFXMg7N7x5vhGPUQFcoQB5yGT8WHE2h9FPAsOq3+8TJg5WeNrVYSiA39syvFdhx/X1pv96hsaMyT8LQGCO5wgsVPut0WpebNZiy7dbw/5CS4ND1l/3cR" +
            "YUBP8Xb/fzkQ4ZIkvY24+UDgLiNwwt3goWxs2N7MP7F/pl4F0gCUKpsMWBniCJT2ynpeAZ/7T8HzroPnTQfUUg9HqI9sJS3eGnm8FpCLioyLa11ORXMaKtvO" +
            "zFBguguE+DCHUcQrxRJBeL61NM4UMS3Gp4u3+AHPqeQB7mUrG6gxt3rc/kpDC9q9TxV9TddqEaCMJxgV5aec6ymeUAwnSt82v0wej9xwsepBZw8sK9omWcqy" +
            "/Xw2E5HP397P/5AACgAEAAAAVwAD/1gABgAVGBT/k8+0SA/Ot3RpM+6apX7aurFQyc3Nf/8YqBitba3SRfOJKkYplArAKei8e5p0G9+AiB7TWldlx83EPUyh" +
            "k6CRkjso/5AACgAEAAAAtAED/1gABgAxOTT/k8faIQ+oLh9oaCMAKDROU4MCFR+0+NNklZ8TR+wgPnvk5uErfxmrPtUMUhxBopUGRr/fmJz8A+PwDyPEKvlv" +
            "TwKF93YNucN87NqlpTESnBW4Mz79QV/unCE9JH8WBfHpNek5+ijpoJB9ucPfmJQ+oMh9oaAjxaqToq1C/PHantUp+Gmi/38UqYKugT8snUck6H8USQs6yxUK" +
            "+Nyr1/h0/5AACgAEAAACAwID/1gACQCBF4E2gR3/k8faZx9pTH4GQDkSc2JVnp7NffidDxnCvoC2HWeqIhyDTad345xb951eqLBfLUlu1YehQ0IFElK3SdHn" +
            "v29Et+/LQCAefV7wcWJoGZI7ZENQiWqtGRVIHBmFzWQmNYoleHYpnRaHOLSGJldQNIrsd/2M9hDUgDOyzAJZHj6QMjxWyRSonRqX+4C7boWUOCGBroHTMlJ0" +
            "LX/PwQJ+BnPzOyYvWDjoODsl2fam6GVI0zcly3t0lr3zjvpI9ASfGyf3XPQ6mPCcTgriJdaT1AwnZ9zvofYHK5UYWu+rfbg/UA8dyUQwL2tKxhZEomE9X0NC" +
            "g0iOemRgp+bJQpzYhgNJSaleChaSFsrOirKWaLZK2jYhIn01+u9w78JkTEzZXPweT25UlUE5XgDNCwywNZT0QqigBO2wFvzKgUcwz0b98+er6nqQvtClgR/Q" +
            "n+vUf8/A4n4Fk/MxOHzJxviwmFeLzwqGzsdXXehkHxw4oRi9umequR6CIr9xgM3kxVzoJRr5T0xiWgIjvCtd+Y1q/39wnND4d2bIvi33If3DKAzXzyhC8yD7" +
            "I1Fk6Fxyuk1/b+YgAajD8n2EltYl+zkmjR1gwGSa7oLVl0Bi4JoBqY6pgeJxR3uwyhuSFI+5mm44mVAL3qUNYq+06404Dzf/kAAKAAUAAAA8AAP/WAAGAAwN" +
            "Df+Tz7QkFQRhjdRKJ+Xz/xhQEoBDsfVeX6rwRf8YUBlsYvFL32qJkc7/kAAKAAUAAABkAQP/WAAGABkbGv+Tw+oFj7QeD6gQDCf8kr8BhLvmaFZnACFt78fa" +
            "DT8AiD6gQA9OVFN1fwg9GtUzG6GvCd9vn8faDT8AiH2gYA9TQqsCbwg85TQ7nhyvDczG/5AACgAFAAAAvwID/1gABgA0OTz/k8PqDo+0Qh9oaAJtIY9SBoL/" +
            "e6Mn4pN/Coa/rp6t1zzVWUzFGEQ3TQnxr7LyyqCifszBGN/H2h8/AVj8AwAFINZ3gWN8P2Pz08YNeV8H1reunqxjzrm3kG+U+WKIjr+4w78HQh5cF+CEMiMG" +
            "L4/PwEJ+AvH4BgAofNAx4R/HsYAaIpy/9W2fB9a3rjiBZ/9LJK17IzhuC6vwf352pX8HGbIcSTg/fJDn3F//2Q=="),
        new("Rgba", 13, 7, 4, 8, false, "opj_compress -i Rgba.raw -o Rgba.j2k -F 13,7,4,8,u -n 2",
            "/0//UQAyAAAAAAANAAAABwAAAAAAAAAAAAAADQAAAAcAAAAAAAAAAAAEBwEBBwEBBwEBBwEB/1IADAAAAAEBAQQEAAH/XAAHQEBISFD/ZAAlAAFDcmVhdGVk" +
            "IGJ5IE9wZW5KUEVHIHZlcnNpb24gMi41LjT/kAAKAAAAAAFfAAH/k8+0YBDiXMKcR8yhVvWlixzHJ38RDf38871kt8fUMBN9eMLTJ9VgsRcII1Fop56l3NJf" +
            "RWJyv8fUMBEeKTglvv+DopbtE9qOYww0bYJCThyEr8+0XC4yraBUS7smB6hwIkGXPK45wPf7MflbwHwnQPkGwHyDABf3jYZY60tQL2JrpW8xE2m6e2bxyUfn" +
            "5OOgfwTpBC0mfoy3l0f/f8HzlIPnIwPnIgyMurPaDri3n0NlTVrNEyPgsWF/BjbQN4R65KPPIGt97iOkAk8K31KBrHZY2kueVZZoVaSCP8Hzk4PnIwPnIgyM" +
            "urQGuDCfeKOSrOMuA3eUjIoSbYSY1zx/JL6GMM81wAGacQrfUoGsdljRe35Vas3AGt4/wfORgfIPgfOPAU7qEBpwEhxtqr6MOoQtk2UXeym9HTRnoVjfdoSD" +
            "lbwWc929gAe1LgA6EaMh/lv/2Q=="),
        new("Irreversible", 8, 8, 1, 8, false, "opj_compress -i Irreversible.pgm -o Irreversible.j2k -n 2 -I",
            "/0//UQApAAAAAAAIAAAACAAAAAAAAAAAAAAACAAAAAgAAAAAAAAAAAABBwEB/1IADAAAAAEAAQQEAAD/XAALQkgkV9NX01di/2QAJQABQ3JlYXRlZCBieSBP" +
            "cGVuSlBFRyB2ZXJzaW9uIDIuNS40/5AACgAAAAAATgAB/5PPwEQRUE7NRDV1j5A2UocjIdd/H8B8guA+QZA+cWAMj7WgTAevrXHQHxeGNkf2JnJZEp3JDx93" +
            "/Ae3BbOmFZqf/9k=")
        {
            Reference =
                "AAQKEhwRHhgIEyAYEiUjIxYRJSQkKC00ExUZICcxJjQWHyogLyk8OR8uKScmPkEvLi4wNDpCNUEsMzwwPTRGQg==",
        },
        new("IrreversibleIctTiled", 45, 34, 3, 8, false, "opj_compress -i IrreversibleIctTiled.ppm -o IrreversibleIctTiled.j2k -n 3 -I -mct 1 -t 24,20 -r 12,4,1",
            "/0//UQAvAAAAAAAtAAAAIgAAAAAAAAAAAAAAGAAAABQAAAAAAAAAAAADBwEBBwEBBwEB/1IADAAAAAMBAgQEAAD/XAARQl9SUAVQBVBHV9NX01di/2QAJQAB" +
            "Q3JlYXRlZCBieSBPcGVuSlBFRyB2ZXJzaW9uIDIuNS40/5AACgAAAAAD0AAB/5PH1jASXqjlVO3cElH4mvmHvrINgfNnmUhq0i7B8YwUAFt1GJjCi9FNwoTB" +
            "8ZARUFMaMuhw1azocb/+h+C9wE0DhAAVbZkc40M1dTCAgMBIABzXklyAgICAgICiAPaiwEcBw4AdP4QPSXwt6p0L4kBkwGOAVfAwOg7M91+jNNN8DBobWJLR" +
            "MVHARAC8zaA61AddfJgJNk1mPUqMUasB/yuuXmlDrzdST+DqXgg5QtvBL/9+gj04O8Vmc+QGlhby7RVWps0JiNVQacB2jgOaKA7RgBinLllq91t/1bfSPZQw" +
            "upa4A9QCU2WkrX5zPXfhRXrJ3krrFfjiHMU/okQ+ApY5gRLQTu/5PO1pg/luzcipMaUwFOLerR+nP0Pqw6I1U7ZfwNoMQTPLRFcZKy9d6gYvDflwXEl01QvE" +
            "/ELA6vZNzJU9ZM25yIj8gwAZpF581g9FincZdn/8gsDwkqZ8bTbm/nyGSvxjvjHQD4UQtJ6WAvHIiV3lYQ2KB4u1JjpLMCwYb20nvqkGvBp06VfOhpx0/DJo" +
            "XpckPmx//GQ+EuA+QgAVOREXGhLJ7Hd3feA0M76HI/nyD+d4B0/8UF8Zwt1Wo8PvFiEGUGo7uaov/CV8Qr4hYKaiEuJzylNzsUeo10psrghoV/5Xy0dfiGPM" +
            "FxaOfd/8bqA+SgQHyUBhyDP8nBot8rx/4mrSxdFibQxP4vGyqU5XfLrWK9Gu1DgFDGMjFnzALbarLEbNQMgd+dqsWp1FoigvGVRAlNC+btr+Qyk7fZ+aMvZD" +
            "cFhw46kj2jJ2bW7Q55Yb1lDGeoB3Ppq2S2tJ5wFF7ku+ba47b6vEaSSy5gpnoSeGmlxMqMhTHKO4mTyj4VX8q2lgc085D9rldEL4NItlVJordqnIM6CLegfE" +
            "qDUm5l3LDpQCnX2X6bvQvWDTqL/AfJQ/gpvgpCGTZ+kHNqPB2ForoDWEqxtb3L2X6irbIbSrlIVO9msc0zjjU2p+QrYvBxPVWSObKtLkTipqj4sgy6Pg01D6" +
            "II8jvr95DWdOp41YDZKXjB8VBBftwcYGoSwyVs/7KxOVSOIQHgJDveCcU2wWTjLblnDFr/Lqubbh2yTV7mtXJeMBWAmVO9T569TOX1Q4f/wJvhJvgSgQyCiO" +
            "7MMsejrL49zi6DFATH9HsQ4Nt3WlPEInsps0ocdWBWac23TC0qOv/yOujy/qukgbtKJsiQiqS4lApwr5PbX1+D85V46pZ+J+mkbNdRIQCsAVdfnOkKmcuu1Y" +
            "rdOMEkUI91LuStlFTTxjz7FOX/+QAAoAAQAABGAAAf+Tx8BYHz0wrq3Ea5vPd7HHwmAkupOlp8qak6BFLMDHwkBLIKnVRe+6x8cFHBhBNkcnLqOjADhB+KFP" +
            "lYCjooCo8RZ3goCA/AQA0SprvskecP74gHzHhWLMoGkP+IDUwlbZcOhssfwA/AHD4RCrCmseQm66EMPgH6LDxAAwqvWE0KHEEAb5MWYjl7WApJH1bsIo+QPB" +
            "hEKQRkeNH16SF0ATw4XokOHAjuVr2+3WQdctjhkjhJyuMMPgTh8LMPgYXmbCwMw9zbcSOVl5jj0mpukwOkfrdBDchrg9AP4ychuPPI6vvfmRtxJc/ck+bMeD" +
            "fLqQwVD1Yd1kQ3P3Uhb9I769gn3Dw0PDw+AgkMJLjDCAp1shtlQ+3I8AYI1FVlIQ/ORAnrpiieDucDpYjzjafAfYxt/848CMdT9pdNPcGAdb1xQlOmf85AB7" +
            "laAHdEuzBNFvZdKW0HWz/OQ+ct8w0FCb+fMmw1LzTzrpRNKPKH+YZmVyVesteCXxrD3k8KvYzfk5atbPVEnniUwPr9Wo0KfUyfzjvoJ/QPATdDFKlyFLQBUQ" +
            "dFXX1zlAXHa8yuNDmb6fx5nFPUDtMX9jFA2dEb+QpWKutIHKm3/9gh8xP6CIlEWrgnNkmliYOU4l7dV3w5ZEFprsjkkyJ2g3EZHAuY/2gV/ccO2CIAaf4Apc" +
            "0kJXoLE17/1H/yKL9R8Aru62PQOjO9wB2uMa7mJM/Vp/FZYv4SLcW+99QbxddtWPlVEdSBmStE/I7vkF8O26CL7HkW+xSBiv/HCIvUYPgDA8ujyiupsuYCZo" +
            "AvI3IMIISJe4toRfUVUEjY9yAEkJBU7Q9Fc/YgJtBR5rMNG0EdEnUKWO5Xh3J3eWal47e1WfO4wf5GOzS73JBVcj0/6MS98pnrSFS7pBjRejHIWs43wt90vX" +
            "14JOzi3/C/9YEmKc9XtdZRNMajgjt0fOACTfc2387n5n3zuQ/1+tqE/XY2EBMH5BF2uEyZfEJ0kCvHXD8ggaL2wC9lOHKAZtu8Il1+GvQbxWM8djvOMoS2bM" +
            "dqSbdDqLU9dveCj4XSzwbFBEpeGoXZTfRvh/11LvfHxDFw8b2HedE9oIVI/3V0B8dB06VuUvGtPdapSB52UM50fNlZZE2eMXCHDXixvjWGcKE5geA4viXlbA" +
            "BszzH3vNVjZ8nU0MGsDg7QDYs9DtdhmW/1b0hCDrzj/9D76KV89AIj217mZaWjiBBYSmimWI+p903Z7upFIzB/7D0CO6NjYUeTNRl8FKJQ5v/1cEfKhoHetB" +
            "c8A1LQzmb07jRn/MuztCQJIlmAHEY9JLYUm62v7LMUHINmmnWYFI8R/oHfxKggQprCkWEiVlKb9nzVFKh3JmhFDhDyWMHo/7YZtcZq/ajdcHXkyFZ6dP/YVW" +
            "LTR9SvD6FKJpVHAP41ud4+kRwBPvWob9UMQN3w59FBB5oYcLn4/KkjcGj87C1Ec80LTiHRIhWLyKGn//kAAKAAIAAAPNAAH/k8fASATKjUQqY7wXDsfELB9v" +
            "JfZQGzsrF4VKx8JALoeT27wDSeSAgIDHBxwgmrfBkzPWYoCA8ID10Bzq8GC1ScXCYsPgGgw4YgAtPxRnCNcgTt0gLG+lwx4fATBjgBibz+q6uGsTMOTzqKGo" +
            "bzsR0Kw3/R84wiAuB/C8JDpAX9HhlDDRaWOnkzLOkTh8APrDp8Onw6iANCTT7ih6X8rSZhHPcL1ALSvp+NM8Lp92KpnsSrTqKHvcrm1+OdHXS7HHxsYqaXjD" +
            "o8MWCYCRy7Yeaj0Wknj1RF1Ikvzj6LP9QcBg01vaprPQpX+vqs2sef0DwLg7S62URETog0E/jWNNp/1iQLHWKkB4dWOYozpWXi1+weMzR/zj/oGfWGCGv0Dc" +
            "pD5igcmyQKvAbTX/PBQ26L8YHCbf/X9pZwFSmSYlN1ojT3/9Yn84r6BYtIg+Mp2lAHU+eD41p6cfG+pFf+Eu+9vDDLoGYA9FymI/FCltyb4Uv/2CkH1B8H1C" +
            "ACfYvGixnrHYLZu57Lr3tOmFoyNPIZ3aU2zsThvyGPMIIzl/IaE+wdVGZZ18oxt8Ji5LH/1HH1HH0tyaP9W0MxQd6WmJdVbCUBCoOmjCNRSpMvWKemFDtkJ8" +
            "TbwoQWdbuYlwOB14m0Y4YaKXXCWad13rwfU8hrwWk3n9iGBvk4h2GLWU7FphJjxe6Wt5JV2b8rM2BXA3KHWn/q+WG+ZqPhTFLVDtrgUn3fUYt44I49B/OmvU" +
            "gn0lM79S2VJWNcgoeFY4+9Ez67gYM1qcrbr7LDWT01eya5ng6FSRKJkx6qRlD/0ufpdfS4AwhYPlmU3UhNxPNR2O6txc9VGtUaq+NdyXGcSl6WohFAs1SdcQ" +
            "WR9NeM6mf+WW7Xo5gWJAwyTGi89+bDNzdimVpy2YGLuYlRbtm4X6Hozyb6DbGAytkbGI2UPVdLxTVGRUiG98fax2tNFfv+UNx7fk4KSfLqT+JCRXTXZqiIIt" +
            "sPRr7lZJYaoYvDupDxPs5fplgGyzbENtdJQtVYQvHxnvzYXEsoxapNFMTJ39Lj6zv6XANeMaLBqv2QQEwtkuTKtwFo3HayiYzrZK8fyRXlUjrOYhw80uYqb8" +
            "UvzmKXBoVnie7PfJc1nUNBkDtHo6GLWw29xti1G8sltMjdmshW7nbX5Swl8UBZis2wHCqR1suI+GqXkWuDubu3f9WIz9zEvWpHA9C8HaE7O+rAv/MGWrl23+" +
            "AjnZAehV1VIId0BtaR2voJhtMZFXa7AtnDKQWCI3I4sAjSKaVaErIOhP/5AACgADAAADoAAB/5PHiRf3Vp2QmOiRT8fCSBDbv3tPXoZ9L8eLCN1Pb+iHFY2T" +
            "oy2AgICAgIDEfXbCbPBASQLHwkQxoRAV/MSLLGrkvRWU3Kv3zQt7w4QAFqjF9MOIh0WCQBaETkF87cyUGbqBZfkIYI3px8BUM6E4IEXjsnkbNH2+tgjpTzfx" +
            "Dad9DjCWYHq0BGFqZMZV46CcBjlHN/EGwcJY8bHwoCDpXN4FRP8WaWFD2ly225oO0zRErTjNCzUyRFLJA5idgmL9okBx1A5qCok59mqlA/sjyGUg9n/9YkC3" +
            "omte05Zd5AoPd/hZ1QTDCvf9YiBuZ1c2J0cU3u2N07CA/3tt2P0jvrDPsDj9ACnLCbYu848DciWxv9J04JzfQpphv9sDmxr1d/IpWv8BLN+CUPdf/UIQfUIw" +
            "fUGgIWWAy0iJuOl5/W/cHwcoFxQiz6jxgMo3Qt8+ieV0NmpvDFKPbdJyxcMjJI8Z+f1B/yRn0jDufqhoFeaQcor8yIrfrbM1I1YsRNlcQAP/fZPKq7A9/my4" +
            "c/BX5W39Rn9Zt9hweRmZmQMi0pAMBnuLQpJ2O0vKLlMcGps0FCshDKno651vytEwbWUE81avhmyHCgKtWE5h3aV8KB9puM1fDgxmoH33r+TXgLsY0ZcSrECQ" +
            "s5rlQZSXKEQoCKIWcK9WE8E4WfrLqHKbmWLZEI4/4icOmnJEJM2uou1hk+N2SaJ+6k3QIsKP9Y86MH0rhgY+LSYScjsJ9FyZWmPXgxSeWugdeT/D7Tn6WsH1" +
            "G4Ag8eWw0JBYC1xOHOPRhqqUeDIR61sMEgEftE2vSeJ9d9QWQPhp8A0jwa/kQtxKALJ6G51gTSHnAJ9ZqUk4/odrANg7cDgGaed+95F5ghqmAyPmDpc/0wOz" +
            "R10XdqHQIQZE+lLBiQH4UE5Lzd/KPwGDt6u+NDtQLjg9wdWZ3WtUsD8pZ2BWEzfABgi2k8gl/BoTgNPggbpoA3DgZoqfK7/RCJt67zf9h39Z19LEaDpxjyuV" +
            "eTZklw0YLFIJ/qvUvV/PNwK1Aoie6WI0jcjz7U8a+l1u6VBdytTDkEGMmg81VEHxoyHl/vVmkhwf/RWlqZ1DZy7YTSExnWsAMWtlqSlRRHh5fK74xeMUIEAc" +
            "EvP+rlgH90VIDl1J1ezWeGcDUF4b8qQ+HUxKcC5xsPLvg7D2dBkOq2MMbICHXBn8vXfEbFwPpkp/PadVBKJlOvoUsMqsf//Z")
        {
            Reference =
                "ADNkBDhUCzxaEUVhGzhrEERhHjxtGEtoKUZ6J0J2Jll1KFl3K115L2J/NlKGP1yPM2aCQFyOOGuGSWSWRniTQ3aSQ3aSRXepSnyZT4KeV4qlYn2xVommZICz" +
                "XZCsb4u/bIi6bJ+7bJ+8b6K+dKfDfJfKhKDSeavIhaHUfLDNj6rdi73aibvYBztXE0ZiIDxvGEtnEkVhJUF0Ij5xI1ZyJVh0Klx5MEt+N1OGKl15NmmFRGCT" +
                "PW+MOGuHTGibS2eZTGicT2ueVHCjW3eqToCcV4umZYG0XZCsV4qlaYa4aIW3aJy4ap24bqG8dJDDfJjKb6K+eq7KiaTYgrXRfbDMka3hkK3fkK3glbDjmLTn" +
                "FjJlEUVgJUF0I0BxJUB0J0R3LUl8NFCEJlp1MU1/PlqNNWmEMGR/RF+TQV2QQXSQQ3aSR3mVTWmdVXGmSHuWVYejYn+yW3eqVomlaYa4aYW4aoa6boi8c43B" +
                "eZXIap26dpLFhJ/Se67KdajDiaTYhqHUhrnViLrXjL/akq7imrbojcDcmMzoFEVhFUhkGExoH1JuJkJ0MU2AJ1l2NFGDLWB8P1qOPFmLOm6KPG+LP3OOQ3eT" +
                "SmeYVXCkSHqYVnCkTYCcXXmtWnepWIunWIyoWoyoXpGuZZezbIm8dpLDap64eJXHcqXBhKDUgZ7PgLPPgrTQhbfTib3YkKzfmbTnjsDcmrbpk8Xio7/xn7zu" +
                "FkpkHlJuKUZ5H1RvL0t+KVx4PViMOlaJOlaJPFeMQFyOR2KWTWqdQnSQTGmcRXeTVXCjT4KfTH+bYn2xY3+yZoK2aoe5W46pZZezbou+Zpi0dJDCb6G9gp3Q" +
                "fpvOf5vOgZ3QhaHUiqjZkrDih7nVka7hibzYmbXplcfkkcTfpsT2qMT3q8f7IFJvLkt+K115J1p1J1l1PlqMQlyQL2J9NmmGP3KPSmWZQXORT2ufSHyXRniV" +
                "WXWpWXaoXHisYHywZ4K2V4qmYJSubIm7Y5ayc5DDb6LAbJ+7ap65gp/ShqHVdKfDfK7LhLbTj6vehbjVlLDkjsHci7zZn7vtn7vtob3xpcH0qsf6nc7sptn2" +
                "L0p+Lkp8MEx+NFCDO1aJQl6QNmiEQV2QN2uHSGWYQ3aSQHOQV3KlV3OmWnWoXnuuT4KeWIuoZYCzWYyoZoS3YZSxc5HEc4/Ec4/BdpHEeJTHf5vOh6PXeqzJ" +
                "hqLWfLDLjanchrzXhbjVmrfrnLjroLvto8DzlcfjndDrqMT2ntHsrsn9p9r2LF97M0+CPFiLMGOAPVmMNWiFRmKUQ3aSP3OOQHOPQnWQRnqYTH6bVYeiXnqu" +
                "U4eiYX2vW42pbIi8aoW5aJu4aZy4bKC7cqTBeJXIgZzQdafEgZ7ReqzJi6fah7rWhbfUhbjVh7rVi77bkMPgmczno7/yl8vmpsL1n9Pvss4Br8r+reD8ruL+" +
                "MWN/PlqNOGqGMmWARmOVRWCUR2KVSWWZTmqcVXGjSHuWUoWhYHuuWIqnaIO2Y3+xYpWxYpWxZJezaJu3baG8dpLFgJ3PdajFg5/SfLDLdqrGi6faiqbZi6fa" +
                "jqrck6/imrbqjL7bl8rmpMDznM/rrcr8qsT3ptr2p9r2qtz4rd/9s+YCu9cJOleKOWuHOGuGOm2JPnCNQ3iSTGmbVXKlS3+cWHWoU4aiZICzYX2wYX2vYZSx" +
                "Y5ezaYS5cYy/epXJbaC9epfJcqXAg5/TfpvOfbDLfLDLfrLNhLbSibzXkq3hmrjqkMPenrrtlsrlqMX3psL2pcH0pdn1qdz5rsr9tdEEvdoNsuUBvtsNuOoG" +
                "NGaCOGuHP3KOSWSXU2+iSHyYV3SnUoWiToGdY3+yZH+zZYC0aYW4b4u+YJSuap25dpLGbqC9fJnMeJTHdajEdKfEdqjGeazJfbDLhLfTjancmbPnjcHcnbnr" +
                "l8rmk8bip8T3p8P3qsb6rsn9tNAEpdjzr+L+u9cKsuUCw94SvtoNuu0JuOsHSWWYPXGMTGibRXiUV3SnVHCjU4ahVIeiV4qmXI+sYn+xbIi6YJOwbom8ZJi0" +
                "dpLFcaXAcKO/cKO+cabBdqjFfK7LhLjUj6veg7fSkq3gi73am7jqmbXpmMvnmczomtDpodTwqMT3sc0Aptj0ss4Bqt75u9cKuOoHtOgEtegFtukEuu4LwPMQ" +
                "TWqcSn2aSHuXSHuYSn2aToGeVIejW5CrZ4G2XI2raoW4Y5WxdZDDco3BcKO/caXAdKfDeazIgJvPiaXZfbDLi6bZgrXRkq/jj8LejcHcjcDckMLeksbhmMzn" +
                "odPwrMf7oNPvr8r8p9r3uNUHttEFtegDtekFuuwJvvEMxOEUz+oewvURz+seQnWTRXiSSn2ZUoSgWnapZIGzW46raoa4ZJezYJOvdJHDdpHFeJPFe5fLgJ7Q" +
                "c6TCfK7KiKTXgLHOjqrei6bZh7rWhbnThrrVi73ZjsLdlsnkn7vtqsb5n9Pvrsv+qd35pdn1utYIutYKu9kMwNsPxuEVtuoGwvURzeocxPcS1PAiz+sdzP8b" +
                "U26iXHmsUoahX3yvWIyna4a6aIS3Z4O2aZu3a566cI2/dpLFf5zPdKfDgZzPeazHiabZhqLVhLfThLfThrnWirzYkMPembTmor7wmMrmpsH1ntHtsMz/rcn8" +
                "rMj7ruD8seL/tdEEvNgLxeETuOwIxuMVvvENzusdy+cayPwYyvwYyv4a0AIdU4ajY4GzYHyuXpGsXpGtX5OvZJeza525c47Be5jKcaS/f5vOeKrGcqfBh6LU" +
                "hqLWh6PWi6bZjqvelrPliLrXk8XioLzul8vmqsT4pMH0o9bzo9fypdj0qdz5sOL+t9IFwtwQtukGxOETvPALuOwHzOgby+gay+kcz+se0/Aj2vcqzQAb2Asm" +
                "WYypWo2pXZCsYpWxaoW5cY3AZpi1dI/Da526fJfLd6vHd6nFdqnFeKvHe6/KgrXRir7ZlLDjiLzYmLTnkMPfor7xn7rvn9Dun9Luo9Xxp9n2rsr7t9MGq977" +
                "uNQHseP/wt0RvfAMu+4Lu+4LvfANwfQPyPoXzwIe2PUnzwEd2/gr1Agk5gM2ZoGzbIm8YJOubIi7ZJaxco/BbqG+a5+5gJ7Qgp7RhaHViqfZeq3Kg7bTjqrd" +
                "hLjTk6/ijcDboLzwnbrtnrrsobvvpMDzqcb5ss4BpNjzsc0AqNv3uNQGsuYCsOP/xuIWxuMWy+UZ0OsewPIQyPsX1O8jyPwY1/Qn0gUh5QE15P8y5P8y5QE0" +
                "YJOub4u9aZy5ZZe0e5bKepfIe5fLgZzOhaLWdqvGgbTQjajahbfRlLDkj8LejL/ci77ao7/xpsL2lMbkm87qo9fyr8r/pNfztNADruH9q936wNsOwNsOwd4P" +
                "xOAVy+cavO8LxfoV0+8hyfwY2fUo1Agi0QQg0AQf6AQ36wc52Qwo3xMv6Rw3YZWvd5LGd5TIe5fLgZzPcKS/eavJhKDSeq3IiKXXhLfTlrLjlbDhlLHjlrHm" +
                "mrbqobzwqMP2ms/qp8L3ntHtrcr7qtz6pdj1vNgKvNkNwdwPxuAUtugDvvINyeQYvvIOzusex/sY3Pcs2fUp2PUn3Pcp3/ss5QE07Qk74RMw7Qc84xYx8w9D" +
                "aZy4bqG9dZLFgZzPdajDg5/Se7DLj6rejKfZibzZjL/ZjcLck8Ximrbpo7/xl8rnpL/znc/srcn7qMX3p9v2p9r2qNz4rd/8s+YCutcJxeATuu0KyOQWwfQQ" +
                "0/Aj0OwfzwEe0AMf0wYj2Asn3/sv6AQ33A8q6gU44BQw8Q9A7go97B887B46dpHEgp7Qeq3IdafEh6PWhaHUhbjTh7rWjL7bkq3gmbXpi7/cmMvnpsP0n7ru" +
                "mczorsr7rcn8rcn8scwAttIFvdoMsOL+utYJx+MWv/IOue0Iy+gbyuYZy/0YzP8a0AMg1vIl3vos0QQg3hAr6wc55AEz3xIu8w9B8w5B8w9C9RJF+xdKAh5R" +
                "cKS/haDUhKDUhKHUiKTXjanclbDjhrnVkMTfnbrulsnlkMTfo7/yoL3vodTxo9byptr2rsr8t9EFqNr3s+cDwt0Pu+4KtekEyuYYyeUYy+YZzekd0u4h2vUp" +
                "y/0a1gkl4/8z2w4q1Qgk6AQ35gI15hk16Bs27B888g5B+hdJ7SA8+SxJByJVdKbZdqrHfbHMhbfTjqvehLjUkq7hi77ZnLjrmrbpmczomc3onc/so9XyqcX3" +
                "s84Aptn1s9ACrN75vdgMuesHt+kFtukGt+seve8MwvURyf0Z1PAkyfwY2PMn0AQg4f4w4Psv3hAu4BIu4RYx6Bo27gs89hNG6x47+BRH8CM+AR1Q/TBM+y5L" +
                "fK/Lh7nVk67iir7ahbjUmbTnlrPmlsnlmMrnnM/ror7xqsb4nNDsqdz4t9MGsOP/q935v9sNv9oOv9wOwd4Sx+MWz+kdv/IPy/0a1/Qn0AMfyv0Z3fks3Pgq" +
                "2w8q3BEs4RQw5wM27ws+4hUw7SE9+xhM9ShE8CI/BCBTAx9TBCBSByNWDSlciKXXhbfUmLTnl7PnmLPnnLfroLzvp8L3mMzopMDzscwAqdz4otfyttIFtNAD" +
                "tOcDtukGu+0Jv9sOx+MXuu4LyfsX1fEjz+kcyfwY3fks3Pgr3fks4Pww5QE17Ag63hAu6QU39hFE7SE86hs4+xdL+RVJ+CxI/C5L/zJPBCFUDSlcADNPDT9b" +
                "hrnViLvXjL7bkcXgmrbqpMDzmMznqMT2odLws84CsMv9ruH8r+L+s+QBt+oGv9sOx+MWuu4KyOQYwfMQ0e0hzOkcyv4Zyv4azQAc0QQh1gsm3/su6AU33hEt" +
                "7Ag75Bg09xNF9RBE8yZB9CdE+CpH+zBLAx9TDShbADNODSlbBjhUFjJmEi5gibzYkcXgnbnslMbiob7wnM/rrsv+rsn7rcr8sMz+ss8CudUIwt0QtOcDwNwP" +
                "t+oGxuMWwvUSv/IO1fEj1vMl2fYn3votzgEe1wom4v4x1wsn5wQ24RQv9BBE8Q5A8g5B9BBD9xRH/hpNBiJW+SxIBSBU/C9LCyhbBjpWBDZTGjdpHDdqHjts" +
                "ksXior7wnNDrm83rmczosc0BtNADotXwqdz4seUBvNkLs+cCwd4Rve4LuesGzOkczekdz+sd0+8i2PYoyv0Y1Acj4Pwu1wsm5wM34hUw3xIv3hEt9hJE+BZI" +
                "5xo27SI99ypGAh1S+StIByNWATRQ/TBNEi5iES5gFTBjGDRnHjptD0NdGUtnorzwob3xo7/yp8P2rcn9tdEDqNr3tM8Dq976u9YJtukFs+YCyeUZy+cbzekc" +
                "0e4gw/USy/0a1vIlzP8c2vcr1Qck6AU35gI15QM06AQ37Ag68Q5C+hZJ7SA7+hVI8CM/ARxP+y5L+CtHDypcDyxdEi5iFzNmBzpWD0NfGjdqEUNhIDxvGkxp" +
                "n9LvpcL0r8v/pNbysMz/qdv4udUItekEtOYCs+YCtegDuewHv/IPx/kV0e0gxvkV1PAkzgAc4Psu3Pgq2w4q3A4s3xIv5Rg06wg89BBD6Rw39RFD7SA8/RpN" +
                "+ixK+CtI+CtH+i1J/zFOBDdTDT9aFDJkCz5aGTZoE0ZhJD90IT1xIFNwIlRwo9bzsMwAqd75pdjzutUIudQIudUIvNgMwd0QyOQXuu0KxPgT0u8hyv0Y2/cq" +
                "1/Mm1Agj1Qgk1wom2w4q4RQw6AU48w9B5xs39xJD7iI+6B04/hpM/RlL/hpMAR5RBiJUDihc/zJOCj1YGDRnD0JeIDxwHDhrGk1qGU1pHE5qIFNuJll1Lkp8" +
                "rcn9q976rN76rOD7suQBtuoGvtsNyOUXvvAMzOgaxPgU1/Mm0/Aj0+8j1Acj1wom3Pgr4/8y7Ak73xMw7Qo85Rgz9hFE8g5A8SNA8CM/8iVA9ylG/C9LBCBT" +
                "DipdAjZSES1hCj1aGzhrGTVpGDNnGkxoHE9rID1xKER3Mk2AJVhzMk6BKl15ptr2rN77s+UBu9cKxuIVu+8Ky+cbxfgTwPQQ1vIk1fIk2fQo3Pgr4/4z0gYi" +
                "3RAr6QU53xMv7w0/7Ag76Bs36Bs26Bo36x458SQ/9ilFABxOCydaATRQECxgCj1ZBTlVGzdpGzdrHDlsIT1yJ0N3GUtnIlZwLUp8JFh0NVKEMUx/LV98LF96",
        },
        new("IrreversiblePrecinctsSopEph", 40, 33, 3, 8, false, "opj_compress -i IrreversiblePrecinctsSopEph.ppm -o IrreversiblePrecinctsSopEph.j2k -n 3 -I -r 30,8,2 -c [16,16],[8,8] -SOP -EPH -p RLCP",
            "/0//UQAvAAAAAAAoAAAAIQAAAAAAAAAAAAAAKAAAACEAAAAAAAAAAAADBwEBBwEBBwEB/1IADwcBAAMBAgQEAAAiM0T/XAARQl9SUAVQBVBHV9NX01di/2QA" +
            "JQABQ3JlYXRlZCBieSBPcGVuSlBFRyB2ZXJzaW9uIDIuNS40/5AACgAAAAAImQAB/5P/kQAEAACA/5L/kQAEAAGA/5L/kQAEAAKA/5L/kQAEAAOA/5L/kQAE" +
            "AASA/5L/kQAEAAWA/5L/kQAEAAaA/5L/kQAEAAeA/5L/kQAEAAiA/5L/kQAEAAmA/5L/kQAEAAqA/5L/kQAEAAuA/5L/kQAEAAyA/5L/kQAEAA2A/5L/kQAE" +
            "AA6A/5L/kQAEAA+A/5L/kQAEABCA/5L/kQAEABGA/5L/kQAEABKA/5L/kQAEABOA/5L/kQAEABSA/5L/kQAEABWA/5L/kQAEABaA/5L/kQAEABeA/5L/kQAE" +
            "ABiA/5L/kQAEABmA/5L/kQAEABqA/5L/kQAEABuA/5L/kQAEAByA/5L/kQAEAB2A/5L/kQAEAB6A/5L/kQAEAB+A/5L/kQAEACCA/5L/kQAEACGA/5L/kQAE" +
            "ACKA/5L/kQAEACOA/5L/kQAEACSA/5L/kQAEACWA/5L/kQAEACaA/5L/kQAEACeA/5L/kQAEACiA/5L/kQAEACmA/5L/kQAEACqA/5L/kQAEACuA/5L/kQAE" +
            "ACyA/5L/kQAEAC2A/5L/kQAEAC6A/5L/kQAEAC+A/5L/kQAEADCA/5L/kQAEADGA/5L/kQAEADKA/5L/kQAEADOA/5L/kQAEADSA/5L/kQAEADWA/5L/kQAE" +
            "ADaA/5L/kQAEADeA/5L/kQAEADiA/5L/kQAEADmA/5L/kQAEADqA/5L/kQAEADuA/5L/kQAEADyA/5L/kQAEAD2A/5L/kQAEAD6A/5L/kQAEAD+A/5L/kQAE" +
            "AECA/5L/kQAEAEGA/5L/kQAEAEKA/5L/kQAEAEOA/5L/kQAEAESA/5L/kQAEAEWA/5L/kQAEAEaA/5L/kQAEAEeA/5L/kQAEAEiA/5L/kQAEAEmA/5L/kQAE" +
            "AEqA/5L/kQAEAEuA/5L/kQAEAEyA/5L/kQAEAE2A/5L/kQAEAE6A/5L/kQAEAE+A/5L/kQAEAFCA/5L/kQAEAFGA/5L/kQAEAFKA/5L/kQAEAFOA/5L/kQAE" +
            "AFSA/5L/kQAEAFWA/5L/kQAEAFaA/5L/kQAEAFeA/5L/kQAEAFiA/5L/kQAEAFmA/5L/kQAEAFqA/5L/kQAEAFuA/5L/kQAEAFyA/5L/kQAEAF2A/5L/kQAE" +
            "AF6A/5L/kQAEAF+A/5L/kQAEAGCA/5L/kQAEAGGA/5L/kQAEAGKA/5L/kQAEAGOA/5L/kQAEAGSA/5L/kQAEAGWA/5L/kQAEAGaA/5L/kQAEAGeA/5L/kQAE" +
            "AGiA/5L/kQAEAGmA/5L/kQAEAGqA/5L/kQAEAGuA/5L/kQAEAGyA/5L/kQAEAG2A/5L/kQAEAG6A/5L/kQAEAG+A/5L/kQAEAHCA/5L/kQAEAHGA/5L/kQAE" +
            "AHKA/5L/kQAEAHOA/5L/kQAEAHSA/5L/kQAEAHWA/5L/kQAEAHaA/5L/kQAEAHeA/5L/kQAEAHiA/5L/kQAEAHmA/5L/kQAEAHqA/5L/kQAEAHuA/5L/kQAE" +
            "AHyA/5L/kQAEAH2A/5L/kQAEAH6A/5L/kQAEAH+A/5L/kQAEAICA/5L/kQAEAIGA/5L/kQAEAIKA/5L/kQAEAIOA/5L/kQAEAISA/5L/kQAEAIWA/5L/kQAE" +
            "AIaA/5L/kQAEAIeA/5L/kQAEAIiA/5L/kQAEAImA/5L/kQAEAIqA/5L/kQAEAIuA/5L/kQAEAIyA/5L/kQAEAI2A/5L/kQAEAI6A/5L/kQAEAI+A/5L/kQAE" +
            "AJCA/5L/kQAEAJGA/5L/kQAEAJKA/5L/kQAEAJOA/5L/kQAEAJSA/5L/kQAEAJWA/5L/kQAEAJaA/5L/kQAEAJeA/5L/kQAEAJiA/5L/kQAEAJmA/5L/kQAE" +
            "AJqA/5L/kQAEAJuA/5L/kQAEAJyA/5L/kQAEAJ2A/5L/kQAEAJ6A/5L/kQAEAJ+A/5L/kQAEAKCA/5L/kQAEAKGA/5L/kQAEAKKA/5L/kQAEAKOA/5L/kQAE" +
            "AKSA/5L/kQAEAKWA/5L/kQAEAKaA/5L/kQAEAKeA/5L/kQAEAKiA/5L/kQAEAKmA/5L/kQAEAKqA/5L/kQAEAKuA/5L/kQAEAKyA/5L/kQAEAK2A/5L/kQAE" +
            "AK6A/5L/kQAEAK+A/5L/kQAEALCA/5L/kQAEALGA/5L/kQAEALKA/5L/kQAEALOA/5L/kQAEALSA/5L/kQAEALWA/5L/kQAEALaA/5L/kQAEALeA/5L/kQAE" +
            "ALiA/5L/kQAEALmA/5L/kQAEALqA/5L/kQAEALuA/5L/kQAEALyA/5L/kQAEAL2A/5L/kQAEAL6A/5L/kQAEAL+A/5L/kQAEAMCA/5L/kQAEAMGA/5L/kQAE" +
            "AMKA/5L/kQAEAMOA/5L/kQAEAMSA/5L/kQAEAMWA/5L/kQAEAMaA/5L/kQAEAMeA/5L/kQAEAMiA/5L/kQAEAMmA/5L/kQAEAMqA/5L/kQAEAMuA/5L/kQAE" +
            "AMyA/5L/kQAEAM2A/5L/kQAEAM6A/5L/kQAEAM+A/5L/kQAEANCA/5L/kQAEANGA/5L/kQAEANKA/5L/kQAEANOA/5L/kQAEANSA/5L/kQAEANWA/5L/kQAE" +
            "ANaA/5L/kQAEANeA/5L/kQAEANiA/5L/kQAEANmA/5L/kQAEANqA/5L/kQAEANuA/5L/kQAEANyA/5L/kQAEAN2A/5L/kQAEAN6A/5L/kQAEAN+A/5L/kQAE" +
            "AOCA/5L/kQAEAOGA/5L/kQAEAOKA/5L/kQAEAOOA/5L/kQAEAOSA/5L/kQAEAOWA/5L/kQAEAOaA/5L/kQAEAOeA/5L/kQAEAOiA/5L/kQAEAOmA/5L/kQAE" +
            "AOqA/5L/kQAEAOuA/5L/kQAEAOyA/5L/kQAEAO2A/5L/kQAEAO6A/5L/kQAEAO+A/5L/kQAEAPCA/5L/kQAEAPGA/5L/kQAEAPKA/5L/2Q==")
        {
            Reference =
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA" +
                "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA",
        },
        new("IrreversibleRoiStyles", 30, 22, 1, 8, false, "opj_compress -i IrreversibleRoiStyles.pgm -o IrreversibleRoiStyles.j2k -n 2 -I -ROI c=0,U=3 -M 12 -r 5,1",
            "/0//UQApAAAAAAAeAAAAFgAAAAAAAAAAAAAAHgAAABYAAAAAAAAAAAABBwEB/1IADAAAAAIAAQQEDAD/XAALQkgkV9NX01di/14ABQAAA/9kACUAAUNyZWF0" +
            "ZWQgYnkgT3BlbkpQRUcgdmVyc2lvbiAyLjUuNP+QAAoAAAAAAjYAAf+TztLmgBFQTr9UgZNrP/zqbRmzNXjOKZtnX5O3gP2R1hLEJZiFQQrCDUTWkERbdn1/" +
            "rPNXFQ2rKoOFxWPvxYuqeV0OMy0Ubdjfik7a5RKUEgjEH3/MZxh8fwVA283l6qyhoNHuBAN8QIe2RZ//f1k7ZQcGmDnU+XNI1SrEYiVB6P0gWBb/fwAHKw1S" +
            "KGOPYF8NNggWlYqMnRuZURrJX/9/A7WLwSpeLiXPT+SGJO/npdfwmuRx4/9/wHybVyigwRZigfPjHDKZAuaJERQPnxUBdogmtEaQgAyPknVhTG8mtLjt49VY" +
            "qyntKQmvGpPFzT0/QCLU0an3cDcETOyr8InCKwX3YWoXKSdCGZq5M/9/SDkkoV2hwMFum2rlXyXK306yfAAGUtkDWjQy3/9/VXdRmpcQOcpg/WE2BrvXOKGg" +
            "hpmNJSR//39gfEd3xVeYPylPdzrHfysTYYOnt6kZKNYDVrWJAY/jy7jMTYuRyStfAzpjdt/oUlXYOk43x6F/FzMuzC6ppr0ioX4DFR0wAoNbvyVtCjs6r6+4" +
            "f/9/2NXB0+eHVm/PJTrLnDkfY3fAH/1TcGkvY/9/Pv6FpjdScU04HpIVf381CxfUFdg8GH8v+lqkT62iZ8ZzBOea2dDGZsySkMGhu6kIi4ogGtl/PiigrzjV" +
            "DR/azlOqygDPBuztOQAhWzX8KBm97THmw2//f2RLtoCWHyUpvqkI2IFOznJVBBHtSdp/l/9//9k=")
        {
            Reference =
                "AAQKEhwRHxgqJiYnKi82PzI/OElFQ0NFSU9WYVZkCBMgGBIlIyMkKS83KjZEPjhMS0xPVFtNWGVdV2poFhElJCUoLTQmMT42MENBQUNHTVVIVWJbVmppam5y" +
                "ExUZHycyJjQtPzw8PD9ES1RIVU1dWlhZWl5kbHZsFh8qIDApPDo6PEBGTkFNRFRPTGNjZ2tbZG9ldG6BHy8qJyY+QTA2QEo/T0lGWlpcYGZXYW1kdG9sa4OG" +
                "Li4wNDpCNUE4SENAVldaX09YY1loYnRzc3V5f4d6LDM8MD01RkFAQEFGTFRdU2FabGloaWxxeIF1gnqKMT43MkZFRklOVUdTX1doZGJiZGhudoB1g3x3i4qL" +
                "Ojg4Oj5ETFZLWVNkYGFhZGlweW16coN/fX1/g4mRMzg/SFNJWFJOY2NlaW9ganZtfXh1dHV4fYSNmY6dST5MRVdUU1VXXGNsYG1ldnJwcHJ2fISPg5KKnJmY" +
                "TkpISEpOVFxmW2lic3FwcXN5gIh9ioKTj42Nj5OZQkVKUVplW2pkYXV1d3uBcnyIf4+Kh4aHio+Wn6qgU11SYFlraGdoa3B3gHSBeYqGhISGipCYopalnrCt" +
                "U2RhXl5gZGpyfXF/eHSHhoeKj5aIk5+Yqaajo6WoWVpdYmlyZnNrfHh2dXh8goqTiZeQop+en6KnrrerZW1gbGNzbmyBgYWKeoOOhJSOoJ6eoKWqsaWxqLiz" +
                "X29pZXp6fICHeIGNhJSPjIujppObpK+ltK6qv7/BYnd4e4BweYR6iYKWk5SWmqCom6eerqmmvLzAxbW+aG52gHWDfI6LiouOk5qjl6Scramnp6mts7vFusjB" +
                "dYJ6dIeGhYeLkZqMmKagm66urrK2vK+6xr+5y8rK",
        },
        new("IrreversibleTiled", 128, 96, 3, 8, false, "opj_compress -i IrreversibleTiled.ppm -o IrreversibleTiled.j2k -n 4 -I -mct 1 -t 64,48 -r 20,6,1",
            "/0//UQAvAAAAAACAAAAAYAAAAAAAAAAAAAAAQAAAADAAAAAAAAAAAAADBwEBBwEBBwEB/1IADAAAAAMBAwQEAAD/XAAXQmc4Z1BnUGdoUAVQBVBHV9NX01di" +
            "/2QAJQABQ3JlYXRlZCBieSBPcGVuSlBFRyB2ZXJzaW9uIDIuNS40/5AACgAAAAAbJwAB/5PHyngR6FLPbDD8pjWycWnQXkHOVzDjy+UPiz6hf1P9Hw7Hymwk" +
            "l621ogfZGgtKKbOramvNsc9c/vJzuner02XHxFQpJkOMCL9pL5wAn3Il2WjYS1Eh/vvBhwdKg6SANOeVHr3qv6H13KzP8ysqvl3ZdllSg8HRYfEQgTg1h9Dy" +
            "Typ8QMAtW8GdhC0sj9WPPCQaQPymoaw3wJYeMg8YJMViDM9NG3cgd7sZDdUON3g03+mjidAD/H78EZzHTIVYgy0ianso7G4V20xnR+yCJyltlf5/V57HSQB9" +
            "RUaDR7xmYLHCeFUAWKtt0eo2alETdFlHNqm6hjzHXY63HWiLsk/PpCbbHwPX7aLjgzLYOJRiwzFEB1aI123pX8CfbW4QU4Akviv7OFeIKsPzGLV2JlmVJ7cr" +
            "Qs1hJC/sAJ7fTvTuBXZmCD9Om7H7QG70LES1oWjC1IRqFx58IpbuVbHy5s0YsLSne/faNldkix5Vv4GjyhcvB8QAUGHC2kdYidbCLWj1rSDOfA5rj9yoBKAE" +
            "JC5PyZiivPDI2OnJl3MvnAVTSniEG601S5RzKUpvIjrwwPnABMyXhciwXnh59LChIXhE6n9Kl+S0afwm9J6M2JH9PcCKQOkmL/IZSLEzX6YkBBv4M+Uokuez" +
            "8fi+kdJA6LcR+OwCgpjp5pkdhevz75/b+t628wzONKYFCPivgJ4MIjSikKt7k8xUOcYG94wfbeXGfFKRXvVfH4fBRqHH7FMlLt3vqgO7nbFl+Y/sj61pTZjC" +
            "pw/sJpkTs75tOBSpAEPRcwf8mSaoylTg+HKP4RRWSD48ciDmxH4NNN0JxOK3oSSVYp6nAwcDIgQpHcppcGp4fvm4epYfBRBBwCA4L7i0seIq3hFT5weF3ppZ" +
            "DaRktVmnpgBGz+D82ro6x13hULXZuS+VBLSVwOPQkMtuDQ8Aog320jVUfyG5QlI4JB1HpEGBbUGCEjVY7zy7eFHZiXv2txTV+x9/3Y/eKfWumofBQs7IxR/3" +
            "G/JeeiC8wvGNerUBjR7+XDpwbM2hXaNYEjP5UaZK5ImuID4zEXp670Tb0G92vcNYp+AlqDNtJHlESZvcHBA1F3ECwsLQDv3BBp1kgw4OcRL277d9vceOyJlu" +
            "WypOle1mWMu4ejZI/sNWo6ie1hmX8bchd2t9d5eFBsc7BKqsYJXako6qZ1rQhaTWudMBQ1sydSZ93dQFbbjje5LC1kDOJ2f8eIzRrTNW7ukqvChIWgV1EJoG" +
            "L5GY1TXX4uO39JA8bnjq5M7FoxCMiS0TngnK36DyI6sfcSluYb/P3PrMCibUW5btdV5he1Jf9Jc/tDRvZ6rfiJZGlHBLBMckLaQrddkAwqSTDquEw+Daupnl" +
            "UIHpwnk10oiaflAOMf4MQ/SO32M03kbWRT68jCv4KiQdEr/JKKF3bsBJNORJ4MB8utWdY8+KuL0jJmhZ+gYk3Igyx2YQ9YmRbSvrpOjIcv2t129ndACSBpBf" +
            "W/8BwMJ/JWPJYLMCBmoxeWXr6fcYvZwSCw76hA00sJi9HV0yHpmq3SYuFnl4PkiEFMLqUEdL6Bk/3zDI/4AaOxn7c9OVCFtonaVVN8XbnAiAU+JvVsE9+xq+" +
            "5eH9Jxz/M54w3oU2k7vXdnTYLxWhc6yUlt6iSJrBazZguCqzzbMbMNHAKHqGE9s7egBWB9uhpNVrZKFU6rlXufZ9t27nm/E2TGcaqEFMu8MUUZ44RXCAn/Ty" +
            "PirXNSCzigsOlX2S/iknZNfCemrpNVfxpm5ME6QsqvNC4sfVhTsqHQMe/wrhdpg4S/2+KCZH2Le605uOxwmaiwLBjRdqm1YwIRaL50byDUBG0BeRcbpDS20t" +
            "2+fhvvR+29hg0LdTj8i0a3N3G6LQgw0P6KAIzC+xKiQoUz8mEzuZfeY9+948M+hObOqyvlcy7YH4KLkwdCmwSusY8SUrRwt5wEPle2KzaH7qImAdgpUxCC4A" +
            "SvYtP+KDOY2vkUtPWzqajv0HgIjpKdzUFF99UPc7N7twVXUu9cBnFlH8tx+tB/Pog/1D4PRD3l8O8gNJHvqIRstDTp5X+QEvdLREMhrjTppv/V79Q+AJmNVH" +
            "pjKg7pn9yID4YfOhQME7DSWQHr6jKKfz1wQf/Oc+g/8g6AYYZvfQI+omN4/ok7CoDaCrG8q3iFbEyGEEH1+xKC5NPMr/fkFWTclzCPz2Gsa+Mn/wHl4lZWy4" +
            "+FgfII3InlnJM/jieBYz86sNIPu+ZB91ozC9Wi4FBq/85v5zf0HAE/I3fk/6/KoSb7w7h26x/2mNx1KWGSa2xfnQvwKkHOuwTCntiwLS7Kn7UYJI7DQuigSU" +
            "mM0fjdbrNDwuWVomErBb+fWQruEIkNuKG3WVJZnyPPznPoPfQeBmPnWf0+vfnxRlT+IYb4/pOJ9UxiKtCubr4ZLP9PaAcaC9Wci66EmkaCQYQrCg1GcJ3rBb" +
            "wdcQiqnXJMRhTcyc3axOGItzv2U85U9izcxaxL4H/D1rwnnR/RdPnbfnaU3ONzlYjzZC9aDJKt08H2cPMyqZFt1WJdUirntRwWAssDxmHbT0frsxmWZcXOVz" +
            "RpEatL4dfY9CNHUn7/VWiiP7g78lsk7srPYD5kJQeomqBiBC97XnFUmB3NJXWNvEre+eB4vh1xIP3KI8LnS6VUWQzo3rtRpDsd3i6vtTgDPiNARew28FTSxs" +
            "XOWmTns0s8AIAHbobUKSUMFr6iPcdAhPnFkWXTRIgnTtv6hrXkIDP72otT1PsfvgP7ZOauUn8O+SPdxZm9zZrIDCl4uhOA2a04Y+Au6+byD6I9n4tHPt99in" +
            "Pz66p84hgT/97Xs2FdQ1xcgevKuK1fMvQf1RwseVhCOkQpj+DYZW7J1kSL0mtyrfT/Jq5Jahk1PYS/JZOOYdgFEZZXvTKZdFlOkTAhEr82kxOc9YWRMTXE3A" +
            "ALX45S3+R1/nREejIPz276HP53At6ioahnw015lUYJe+WNCfHPv2jHTRugb3uze5UVgh2PLQTK1GIQlK8TNw7X7mcqlg73pFaaaP9nyuvcYJSK1ePRmBX/65" +
            "x/wM9gubOQos/LuNo9eAzWiXcGAzL5yeaVfqPa7+DT+vz56fmgbZrB0dS4qZ35h+LClTjAaaQmAnv1BUlkltGYb5fLfb16qhy9fHbjLNjGj9ANJlAg2iSJvG" +
            "ybEZbnne4agV7BuqXLwjbtBBGTVPLT1TyNoPj+DV1c+jLlv9rYlL2rD5c24IqXKcaNQipSZfxccUt4iH4gQ3Ld+8haq7OO6DTaudzNZ2PizrzRpJvyv2sNF5" +
            "VX4891FcP80WvktcLX85Ky8ow6Tie0fbVWYJ1LOjtoBYIuW2ZiCl2H1Z8fJ/vVljwQgNdEQGDdrzKZ+IaCou8Cnk5aHYVzjWJu3Jqi0tVtO2rsaCqLt//Rcv" +
            "oc/nddzqeWKuS8OO1eG4u5gCf3l3SULpaenLrDX95tb5AhNSDkjBTBCyyYx4p+aeXxrbNzsuIMunfAgBZJct2aamFRS9CM2SlT9bpSyl/z0i6rmIusvaoUR7" +
            "0SnIj2EhhP8PvhzkoDXIls/69h2TCkU+HIvTp9n2S74pP6VUt+ShVrfFHzom3gqlErcCTb7z18wJ77gF5I8/wfTGUVC+iC4JgbD0K4lAwgHVtpqJ60nHz3Gq" +
            "vTkQH2T+l45iccXwUaWDORn6nDj4sFBL+oVBwDJ0rdfDrYNydTHoh9TugcfKFRpDewI8xPeVSz1OQTvXrkZgco8Q9yIXBppb+qMGGNjrHu43cqZALiPHDOI/" +
            "uVv15re/s3VXUkG36x8OmEZaGWbTOfl0S+CsEmPKlEQOZw05kJSXBzPUnseRwqhqtUKL7P95g2nsLXSd4lPJwhWvle3jaTgz2GlVhffaM+/9G1H0bZ/RtgCL" +
            "YiKXxHdObRSXTQxXXi+Ijzce1oD8QdSpUl6DrGkRuQOWjsPH/2TTiBofiZ+5d4qzk+g5ihVBHZ4CWVmBTkuhtMMZZZdZ9ulpQRPLn2Mm9v4pGyUw1Qs1zUCI" +
            "Yi5/IZ83lvPb0hTs6BE4zFvt7AQ06Fr6ZAEUONfxK/sVR+mEGv8z3+XGZ6nRyVc36kF8F+7T8ln7nCujvFdMJ8yIzWNkJoO14+LM+mDM9s3O3QDrSASZI+99" +
            "Ot9fyyV183Hy1nGowVwnppYUIvb5m123NBUuTwcO9+99wSWA6UnDJ3Js8vM40v1hf/kntKX3/cKVIBQ0tAI8eLljeHXBoFCHmNFq0IWO89qN1v7pzyHf2zB0" +
            "ZFECp04x68dpt3v4qWfZXjQJR2x6y/44t+vKFWoEALT+I6bpb311TK3M2vN7ajdp8K4UMzpOWJWfarxDHyr+BDEcipGq9Dqkl7qd2LmpiHUkxXFVB7gpwg+M" +
            "+aajry+ju5lNxKS4vTQoEm+8Tj5UJFgv+r//N3a0lPEpDAYEy6CApmaPSKn5BpZdJqLY/wbwLZmKJYb4SXhoVgcH+xIBHFR0Ln4TJKxmR2pYS5fDpjKFScBI" +
            "kvtwnmg31CZBJ9f8R1+SdxyDEOWrY6euX5cqH3RqyjKaw6js+VR3NOWpUW581t0XCBdDnYNmgCPMLcUssNgk4yCiLsCrpS9frCrsVpcFAdeTEDwQoksLSW6g" +
            "SZDDRfBGU8x/wnFi9Xt2g+GvBqJGYgL3fxDQSsnW15AR+0gQvj9ilx5aR5e0tjZxeVsaS/YMdhZxe0TKNp+U5uP4bEDoP6MwuojMiHwN8b4ELK8QmKJEiuZN" +
            "LkYbEr7WBaFPdMncmRIQzhb3B+pvwDmEJPDwZjg1igatxrIQBKgTxnkzKCbPKEcsAHQL+tdWhxqrzB11wZd0XRqbo8iNjwmFDgUBTthabINKeClUXEI0WG1Q" +
            "uA6qNM5etn7HSKapG9B09indeWLBrWzB7TNv0hze4P2Rc15hm7HO1jMtw79qkHL6/iOl98xnim9PI2yGpy1S4jE4Yz7ZP1vZoyxFvZYmTx2jX7Q42RzfPJFg" +
            "j+5YFJBc7k9jABa1noTLriUTcNF/zmPmgbZWLo1SWJlkKguY1RmN07HlvzDjhQFbZFuqgOHMXXhjwOuHwX3QYxQLnJEDtdZI3Fr+Xkq3v79NLW06UJ8PRBPv" +
            "7AW2Z1RnBXMUXQ0ETeP2qV35k4Oh0FABQ3TCl38HRlhZaMyjZ4oUQkJfEgI9PGXMZd3G/hRAchD8eVUW5YqpyeIe9qGB9bJnzUFPDNVTQ44ILokonc4DV9ND" +
            "0K2mpgJiJToibPCXc5l2+TDYOVpNkIrU3zFEwjRv2l+4YlImnQAnNnZqict/nTuADhT+iHA1lVVHNQIfAYQwOffwOqRvIP8tFgv/TYQjeCz4NvFPeAYXj5sC" +
            "EJM8kGAKPNb8TqGPVm5jrDbyUFUgad3zCUMlA6nAwCsjX/5l8kBlTLGAofzsc2ulQ21rdCzMAI1KOhnujlwVloJSxUzmPkspsBzNX857xqWPiAmoObTVLacj" +
            "26Fr8+KEMQZDIbUqubSrjDMxJCk0E3Jm0S2Hpv6BtyYl4q2m49Oj9YeCXoQyLiT9RK+pzzUTf5iWsGMn6LYg2ncRPc1PiCQJd+JJC9vv0C10qJH9q79BkMSX" +
            "j1L5Vs8LGPTaQI3DDPUkR5JYFCdtlBa5RXS7jy1f/Pa/6Lc/Paxucjyl1XSjkH6w8hybbT4Hv1wFQSv4JiTOEOH/huTJ3+yJRo5cvd7aRTt5seaG8MCDsHJz" +
            "GgI1/yN6QG180NapmH4xEqlRyjF7EUWFDYdS142/k1p+pJ49+Ud7YNzuqLqpJMUEYD32fEJ1TsCtU9GZ66peZ+PikgLb48UpAD2APYkj6FiV6S8LvjUjsfJL" +
            "d5UWKFU42meQu5J2+PrTjFCiWBUWH5bSqTkqywjel+1tTxBBWmwUjpwxHwbu36geT3buTNNSjpyjk6fbHINnYffodNF38FBEFCyOEctSzCoulyvzTthtRE16" +
            "dl7s1yjwEHLaqf8B6zw8KDrqewNohjxNlxGgviEPXBspyRrrAS3ZPZeYVcH1Eoaz2vt5O7/iFEnCxjLFvcuM9YQcuMVvWNCIF6uVmTfGvHYDS7Qyhrp80YI1" +
            "5a2vq0eO72bnx2k18YkUOtpNA7qDjNdEXSnaj8Cxe/uS/yQy3mpgRxIGytCy1W/u4Fqc05NSJfI+9EIY0g8/HDrIqR3HDsdKLINftp7LgxMxEkC7OtXpkPDX" +
            "D3vkSJLrQM9EQ0WLkcIr0Ux9D/4WUnSK+b8FmbCRS2eoLGw7xqyVQEk/PigujGxOJ44RzZT9cZq5bcRrdpG34mKfMl29qkbIzPo4FUgeoEqF5evXTPeaIJym" +
            "aF8NWMzaEwJ88GNjb3FqcdU7TpMueL8CrcPk9o71A77hq+4CMJ1zDblXsda34BFHLZBees9Wn3PnpcKLyxOGpmlmq4jQ5y+1qVZbSY5uIheVPur7DzPvCWON" +
            "GLIcE27qVJ0CgQ5rb3tYHfx/rn3gCdnqBDP4dWNh2OrwLUssQH22iqYVYujP5Abr4kFRoZE8NA0uKU0REQXyxxqw9baMxzbqq7joNGcMrE8dvSOeJoM5brhl" +
            "xv9J8OfZrC1md6azT33bic7YXy9tvtO2Okny7iP5bZh3eRy50yehhogeMU6uFGjKaWf1SfEGvPwDn5NC3NEPyRW/f4n++2+Apju3HGW/fBtwgXrKnJQNyuUP" +
            "LGHzyTEzqSubdQBOYh+rvDIBFKXy8pnwJNrlQC9qivWwzGgcMpj/X3uXLK4MY6MhviusvrZSxENrnmtkbh4m5ayVX6DfMfvaZSl8Uxbei4/wpPHyXWb0ef+C" +
            "takn1Sm7pskzW3a2d5LYcGPOhDmrX6t7wlKd7OuJfPIUks+n9MxjvSyLP366NRx9/KOMo9XjMPgnjTV+eT1RzCKbT0mUMJ1396CPE6d99ZtOwhk5B5xQqrEF" +
            "/vN0OVOmESJebs2FkjebtFX6pV2k00uNDiVmkufRqjUUhR5K9CCms35UGhMHDpRNZgd0ZAif1FAG+bcZ3iW9cMHTswDkFgoBhViH/njNvkBJZLanZblT9owY" +
            "wK2Z8q79guEzUSBr2z+uboxyIzxwyRcC5h5tr+MO/qxf0gmjM4roYjYuWyxMqaTRrC9TEd+Pn7WpkqkLEGS+0VcjL2QVJ0/k9fC/i3bkYnwegqUvaKOYIPXj" +
            "wajSJiUIYS5QdLJvfjnSs/WPI0TwF9rqJUKOkCyejcBjjF/s4HNPrPlt+Rwu3YHZnC7oTQf7RUY4ojFNgSbVEzTICYB8MJwog/rmrVJOO3DLMhH+NUBJsCQN" +
            "+Oex/KoXSMUFtIniaMOfMd1NS7VxL4a94QWTnk1bkjY4ZUqaUL6OWQk7ZRx4KsOFK9yZNpaa6gCPvVj1TSGQpJGgA36dyGzIvyIgaXo//RdL6N0PpvGADYwU" +
            "s4DxNB+CcSggUNraIFcaBHV6LLwjovWzosTIQmJqm/x2gC/bQ2QiEaRKN8UJfk7rJBXKnaE470+3q+WHWt92SCeMoajlvI+O5yGTp+N8lYzj4+B56i1cIZwz" +
            "Tu5RsLj9RNifFhkSK1XlI/12xfznLJqC71jT2ogIASCb3/9mrU7TsjIOwaetHLlMN3GnA8dq6smRJ60r12SB4oj08Fk7+hWm1/q8td4HNVHU1k0cUXXtgSHq" +
            "rPB0avd9y0UzWE8EK2ZMqP9+GJeeUTYsDJXeXVyFmBgtmOPtohnBeVdGOdy4ZUAHS6TVLM1LeALx8sVoL2HPL1A/dzz8uPOwwD5r2Rx7DZ/WO08AkAfcQaKC" +
            "Muzs+wsGBus1ljZLtgHn/EPiutn2AO4vkzjiX4HTVu/0hir/J8AarSRvzoF70KL94TLzXv6XpC5envkee24m1nGkwxp+9vThEOKeKb3q+qxV0hDeiUhcJYpz" +
            "ZvPXaYftp6qFLkIq0/9SbccKTMP2X/XhBOCntQz6m1LTqc8P+11jNAnWlleJNGGOMcWw5PXVDYDOgF04mztPdHBomArwVKfqqKAwDOE2nYQdXPKoSs911tGz" +
            "O3CvBT5B3qXMKlYInyXh3cMapHBkb1XfOWwRpbSDppqXDs1/b0fNP2hgkBqfRW476s+/DK2P6awBrsbUssbd0A0N40mFI85GdC4WnxwevuULe7tOQqVEHR6y" +
            "X9fyrVNhNdPgZUdU2pV0c8W1r60vGmZwoxVym57ihPI0CC8P4iB4yqSNm7aUlol1NwrT3I+jEQ4j/3e5n1mdDBU58VWnyagI7fSEdENYO/IEnu06eSs2GKGr" +
            "1Ju0RKwS+0rf84SZzOmJhzUXHUcWQ2HL3+5J/2oc9ZuzoOg/ulhaqZnfoP0jCq1i2tPSEphYdZPADQ5DMclGhDWXXojQRcbcY2pJ7FLwFw8Y8iC+Y1T7U/zV" +
            "1AfATxOZfCwGUEmaDMmvnKLdr3pomIcfxRhtw79SuMOldzKBjOXh0o9e/LPS5K53Icd5p+nxdyhmIHHYZ8fZfnN8GxxMfAGAXHlzA3oFB4ta9wybVp2pSlHn" +
            "jgpkPxsJy+eDcxJxZIX4tEeJWek419IuO1XdV26xJX5YK8FLZ+e6qkJuGONKZ2ZeCnWv2fnDwDePLlrpKJxLjUluTjyywexnvUUZWNUp4TteLDBakj8ohXxB" +
            "JklwPp6R9nrgHT7n7f8MYqxKsFc/JKUc0YPDI9ktN22UuoUBwE1eUB2dLw8ilAJt0MVGuAJeYbt8WhRBJPtHwSKWHr/xNIJ8O7yJmvOq2KbxZ29me1EraZSK" +
            "zzBK9qxrnwdAHRKr8gEqMcAbd6gWtO/oifZ9NqRI1HY43bxw/TIQl7F4ZICODmpSwH0De2agu+v8W52wsquOtetmXczZP9jsCpcccRZqfdsCSp/vGNmr/3gx" +
            "kBh3vX3HDux3LEkn1R0rExEnuQTGK+QKf2qEAiLr1Dd0CNfELZxfG7RjKnJAwc7BXCrU/cXZKQL9A+Foh+0FalXytAxaSWo/tO3F7Tu7Po/AmbYFAbbhc6Ix" +
            "hQwO5oXdWRtETrtkq+BRMNJpkn+PQvGV4U668ghVJTsGely3Tgzm5ToOV3vDUP+E+3Hygvj/T0fxPNJZuh8zRuXCvIiiOQCgwGYzE1+5e0sRPUaMbTKA0ovp" +
            "kOEk56PZljsBrmHyraEujLU8DjBiHDYo6icCke6gIGxdIkT6ynL0mYFXK57ou8oS7cGgIlGfhbnWx9QPuvAs9qAk7IRTH7j66gukPswxGIlSzsaclHlRBqMv" +
            "mfD/Dig/+PZBgyQBcmo6WCkAG4etPwL5iGZmdaJlUqXcQqIrMiYNeZhmVXvyfI3l3It9AAUMkTO3tip6/5AACgABAAAa9QAB/5PHxnQH1cSNAxfqXYS7aIIg" +
            "V85+nEEFG3ibptL5b5zG5cfKYC72nUMaichG53Hs5+xxTaqvwEdeiU3VocfETA/rnizjPfEpu1px0OJKvagcw3vB8A0PgMg6MBIyQDmpJFbsiZSERE7FFuNt" +
            "TTOJawQ7pcHh4fAZB0g03QshM/uOS6BF6MYxGH9VEC75NNLBL6RfhcbBiQ+A2DhAFMcEqlUo94Gj2QzW5NY5iDg4Kmh3x0iFWIs8290C0I+2FN+ZdX2uEzNF" +
            "Hu7CohUggvssvynaabZdNqBYiL60uGzCohVAipzRB79yOPcWYhAJRP9+tSNjx1iOux1UyelGayTpp9O0DB1HJozL3F6b5Ly3zIBSGAuO6zudq8BFkhMA7Yao" +
            "tjSBZEb24xxczG69KWHQPSYhpQ9Q0UzR7oaZBv2yIlb690jHUYWzHUTBU3K+iKYj3+wfOoQnI/i6CRdchVIlAw5SQneYqNV4/2MAUrOT57QKUT/IWuWjOq/2" +
            "B8K+m7Rrtws3scdZjizMci5nLi0w/tQdKT+pNys5UWubDuUdyjU0jCd7nxuolXeI7iT4gO/7D1CRHJa8yrXBp1xr9LAQt6f8Ajb99FWS8PEfCZ0kdcEFOcZM" +
            "WtcRc+WTB6tLe5fr5Kuu060N03Poesbw/wD+kpiHhti8kXzcFXeq5v1oUDSPqQJw+y690miNU44UP3f8BPifFlAm5LPhnQRa78BUhoklzcUHFs3If6scAW4M" +
            "wpkt9T+A9D4HwMVOFzFoXZhX4wAAlb5T2Vm7r5OVhhCuMWu3MPST7B3i3Jg+Z3W5/GbSipQihKtUM7yHtTpF8vyTspjjtfytL+EbV1lJe4s13fKiyhDtFXbI" +
            "+M+Qw+ChdQS4n5EQ0eVYcE4d6a6HUJ0psljucfzor+PBajp0u8NZtOv5v6mon+ghAdq8mq/1D8hGT2Ot1ZT4pVwDcAlRrdRP5JRBS+h4LBIjB39PCC8oZt6h" +
            "4bciHZd2gvjHS8PgcKP9Bwy2gj9xtWDQrtwrZxTr8o4ItH8wpojwa2qsLY0jx2NUib6YMXQ/y6n6btVph+BFvM7jaxCO2BIE6S+uvvzTDOUaOqDa+VsmN/br" +
            "tx27FRkDFrco8vSL+9JuHGDrRidg3c0OIaTflcFCK79RitNfpyTf8ZHKp0t4BdeGK+HIuLYx8MrIyw52PaRp341Ht5pmlGz7HPiBLWcvok9xuQBx5RrvFo9U" +
            "neYzzMWwn1YMOjWAUgfJE5offhSlCN9qV5BjcWXQncF9z9JCTfSgXngDQoVftwwq2FQl0qJl/gxtUkzIP1fPzMDnRp8NBl8RYTdXRoJo5CSPcVk8Z378Gr+V" +
            "/g185SC6zJfv0BDFIN7z7a2etnWV7uaitTL5D15FgwXvmQT1PfDx2ix9AAaNSGbR6C7TMLc28s8ZTfd/gXZaUNJ1Qc/0B78BsB9TAyFTpx73kAPhd6w1AmsS" +
            "bDyP8KMjVNzAn0IYVuk/sX8i7ZPJr4PjuS7g/TLbaPuPJ8MzBzlFD77FFubsjHiE87G6ydkXZTO/gdjrEjiIcWExcO5OAaoLTdckWzD8glXFz4Bm0IkiWg/n" +
            "QfubM9KhrkPnno0RbC4NThA8yd8j+5l6su/BYN2UTdoW4fwsLfqowHxUqW9tC2euHxH88snHrlfJWhqVm+Wi4NDj3Xjo0CckQPMtQ1plBCtF9RgOI4MNKWLD" +
            "vD/249pcPamAkWTLJkE0xv94US1FUPVs4jVNf/CDd/veccksk7Cg0gt4KmMIrTMC1yHFYJOprAPIRw1HI92qThae9jD65D0j4t+JNiPD6oQ/Fl8j24g8fFKD" +
            "hOJFXzVDx35ucDbvpg1GHE1gTf17fFYcovz7J+bvO+HyFO4ShW0XfnXmOn8jpc03/cQrMWsUPjdwMR7hyXHJnAWDHZYFFO1spN6wrgdhGgiTZrIzia4Y3sIa" +
            "iEHOoqeb0zgQ+iNbJrKgV+629dg/SznGaIRGiiySOBsGOqIDmPzGEb2OSP0HwNGX/QSMaileu+jMoO9Qc13zwEG7vXvx/Bf6vgqa4I/9Q+Buhsehrwigkyd2" +
            "Os8P1FksTv1B/SUxNMQkIr0xCmNJ/UPAQTsZeM9Fok18oDNzGlfcUIvdDefcP0htrGfo9jhf/Oc+Y59B0O1w/uyCFfy+rzHAjee0J6wJ75RU1ql9G3uYxS9N" +
            "OCxUeVQI1gypymhUTumzZxPSI14liIDuQxh/D6C0MPVyAdUOAgcOI2xeCKJHrFvLo+BNAIDiG4D9B75zX0Hw/GvnKwWlpHQKMD2DVrylytdiXlvNPMnL6v3Y" +
            "qDxtEMM9ttc0EDNm2zRmq9i1mH6lGByCeFg3jrVLWtracc+Fp8yoBVDVTQJLveIaxxxhmAToZi4hmgW//Qg+g99B0NoA4Az2vMDbpghDKHLzFD46bhm0x2VX" +
            "rW8Xjii7ePGnVnwX/XxKJSajcoy9z0KgH6dsh9KS9pSFEzq07Wifo/CW6m80iwoScO18orumjWB6K+wgvMx/Vold3/P9F3+Zs+e2ABox2JVPtIzfZARnJDpH" +
            "cxB0p/msDpF7evPnse5as5C5Yg0LHGNc30SGKcbAQXiKd1SXUGxixPaSzvAGfmooT8gBBt7r9EP/JebiqVrHGyaTA9QEHg7fv5+btvhwJ9lImduz9dyh6Dt6" +
            "/eELN/9aMfDkvcn4bpf3Ok1s3GgpTXGVzUl9TBkPsHnyLS/D4FenNzbCE3vFKnzCVMhrW7lkXmfeXlf1DYABITMfkwg9T203Z4QAIiqKEe4RD30lJAB8VKFV" +
            "htez5MEgtHDBqHqyGT2qcxMxl03fAK5qPw3cBMP1VfZ39dN6+x+6pQvpKIOEOxDQxNjtM6cV9tL+6V7n6MAzlh4m7UmByRswmi9Y6qrM1QwjqCWCuqcuWaxB" +
            "LmthupMYJVoJM3nITplHqiOXKCpRqTqQ3Zg56diNlp/T27F+uVSfrwXefUQXZBbXLtfj/O4fO1fO3Ke5BvPACt256dQcutot9u0GSUkTiEjhe+LUdXmnE4n0" +
            "NwBKc6D78W0JvLEbXJnuGH6hUSO8MdTJgoYpqPmg58vgVGutZVR6oQOPEBib6D+hFVyx8JpU/FLa0k1VYzGes1BhCkdG5N+JKPLhJDkOaL8oiece+hpXyY64" +
            "gY5RB7HjzGNS1x799rlCEsH5ZV6Oz7rl0Py6DnBe6tpxWs0vDjOFJ/W8dTP0ViBEgON06O9L+WsjM4ldpI3vd9DyaEwi9oj5Ky5zblFe4tjoSE3TG8KJSwiB" +
            "HhLBy12/vp/xMM5ZZnLEL0R9ToUYK7pueqF3EecWmPvRu38Uz90uhZhTcMZn6B4S6ndkh7Yf2Bg9I8dQ08Vf0LMhb+x+hAoB67LisVl8wnyFps9Kq4f6jCzV" +
            "P/Gv7mK/JEFT7VoSvLeD199bUDtEJE/C6z/87n9Dn893SMrS214jA8quQQJjefS7Xjgvoy0v1gkuLN8RNpJTwKU/EZA/1x9zJC+NXYZ8OyEZWGhrfYMP3NFi" +
            "oS+epG9f1zotyBXjBVfkYP2XGcRb3wDDX4LoZr0SNktzxxF8dPYyHQzaMltd932+z+yGEQykTJyDfxRY1ziFqVkcqtN5cN08EMN8CR92nv3Lgx+2xNpbxGXr" +
            "EqI0F37BAuF5SLUaInjIP/SgGfeaVx8NMna+5z8GqmNJmpJOOlwHt/8idnT8bmmGKGzP7QcfFeZZoScbG75EFe00EUzG6SYoZpXXuTrIJ97LtH9DH2sTUS7K" +
            "m+07dsDZ69/4Nyxk2TyZxwitBLgnuLDplfXp+nXmxkHQRRY/I/NFkwK9rkmdbhRgnrSN2ortB2ZJ9vm7Q0ldDgK892llD41WSOXP6Wadd6lQGizAmXnMXNAJ" +
            "cCEEXBLM3vpUYFgdESwurp9QVyEl2f0bU/Rtd9G14OugDLK27v8OITz+1kzCNi178yu6dVXJKKYLwOU9IV35K9gptHyDfdHLUBrGpHgluUYBplrKgBvjew+6" +
            "KrSWFUbNtZsXfROC/vDK7kWHPIKq/yUlpJHRBrTipeGQK9Cnf9yt0jli7eDjxKr+A9jrZmTedC1JR/KN90ekEHZHBYcrP+UEZQt//y9ShTtU1UNyh1HBzc0d" +
            "08fJHK1eQf0gYquGABEgZWL8pUZLeGtNjpmZHhr0L9DJHghTUG93kaj6D3ndbQJbo9SkcRadmKxhGN6GElpoj07iVO4hk5yBdBdfbHErUYv90Z43IMVoecVj" +
            "C0rJ+EPzJWdBdleR9j6zYwsDhuFEFtsipCLS5taNbxsaOeqUXGLWpynB3qQ44crQSMvmp68IlGZ7JsgLSohBG4FTyw83GkYib01VrvSCuRsFqieumOvUlPeD" +
            "8kqljO8umRkUJT/jkafKlC9nUkvsYDzDgyySv77C2L55OExUNPE9KtLUneV8CCAhXk/8UhxbM6QRUAHFrBFxHBXu/dM//S2qTcEkjO8oupl75vy0BziQtxSz" +
            "WEZp/HYEJfShu5F697xNh1VRmKMaFqLElqnKGSPhWZVLylJDXbp98OwBrDzU55DL1ub11FlZHRGWrZO6ugUTTOL3VoY83kApV70E+JXeare6QrcvTzuZLL7D" +
            "ALVzygpqJnm6Yt/y4v00/1CCPI7rl9LbtTdwm5n4PPid7aKnn0aP8foBH/755twzZdj5qVnqQDDqVoac8qDfhOT7AUhYsCWD/mA1Td1AGuyoJMsGlAuRR77U" +
            "FomKFM0OVS+oMsbmioSIhQbI2d/BRmt0WkJqBAwHWxB5GVyoBChpPvGWKeIcz5BkUnZTC0S/nTKLxKuf0yoMdJ/ovaeE1uITZ8aDrkmis0if2R2ctAHtyAOK" +
            "DNmG9lmDnTTKi9w/8SISHcgaqusK/zv7ld/Wt6/NzTqHse8QvMH7idWLr/vwV2EqyFPRTacNZ8jYCmoIAKd8D85/EikkXKuLE28JHMQxJE3/cYFgonjWWebp" +
            "sNoiMGgFr/EbulRpGw4LlzwS6lJT4aPQxndAbCRUOxQa+D3V9IqH5Yu6+IpcwgEZGr0i4OxiV377Pek3eS2nSMqmmTMxELWoZNPi35rM3fiM2BGpUhHmCSzz" +
            "7t2UfvAF0DKYTl4abfjM+xlN4yZDLQjYjb2c7wznuCjC0cn25jv4vgZF7lMPwBmGpqqkVfyYdDZWnF+kqc6pDQNGqmVlWw58MkiNdvCIXbJjDEmIKbP0uizL" +
            "6FJqrciGQqavX4olKe0+iu2GJt1YQqhcnZyNXWAspVRNYEfsMsSCufDdRjMEqveoWxzdsX6Ag+CsaYEDCJNXY95tkHd0OuhCUDeP1B9iCCWOdD0Ejhl8hLid" +
            "v6uWj1CGNkyt/xTZ6yY6FKYxXCJ+t1RVBo6BshWdCNmK526AR/BfVAqbHOH2tg4ysU6Bklb/SUa0LXegbgBCm1HnjO+APatRGyWei4a2AWau0N9HswG6hd/v" +
            "a7k5aKGef17c7kSgYACdo1WDTiZWeZsoQGr1vYDMyuhpBHBx2MPly4u9n48uhR07e2clq5ZqbGvHq5RjoJaGdpsUe4jaPchKeKqJD1xeJSOTK6CVbVD5xLa3" +
            "Y7t7/oS3CtbJnd/XMyccfGE0RfcjDK3EkMBYQR2+kXWbLH3/YNjYPAAa0NSGbL71aq28sYhjSfgo/NaX57V/NaLnP7Fu2xapYopULYR4yA3oelZ9a3kzMYmX" +
            "9pokxIcRIFgMBJpBEDxbHRWsSsiA4yACwlLUCCi2oC6GjaSWC679NYowEmvdSCbjv71LA0WUG4XtDDC/OsCxCbp9MpMgy5GxoTDzTAY/YFJWDdf0Tnpw+SlO" +
            "xJX90HWZv5homE5uwRoG6p9pcjCkwQemUWYJhsR1iNHFx7uv4ZJD0BH7H1tI3aTedFEoJ9+1Y0LAXP76lK3Vbm2csYFqYSLgdVJlesdKNH4Sp7+bd6UGTE2S" +
            "fWGqRQHM2I4cpQfn60nmzeZkEwVvFS3v28MWH/g3qajl/2Q1s06SdiOLH8bM7KFLWjsxArZsrWeuhW0UM8kG534kIBgtnv6FvRdUU9nYyfK2F28APvXW3bRH" +
            "w0rjMEh3Aj/Ox3hM787iFS7xcprYpsp1IkD+5pQbC77oam/LlqYoVpYzR1uYejRkk50U4fJP7drAt4DqEE0L3nIcLGdb7+OGFnmuZqoLGt1jVPc2hV6jKnnQ" +
            "NR6J5ykUDmklIOm8VHQhRZNHimWGY9WKeXw5ctkSXErMfQ2fJydmaKp0z8Pi6EahzPajZkUD2/w2UvqPKTX/eXmSn1bcu2FbGoKo7FLRG8eQwnXRg4V/dvQS" +
            "Gu3tg6AAvsZ/Ry9BggIrVBXWS+J+rXBqSiJwdfAcNfyOmJyjbwpijSSXoNsthxg6Xxf5/1RE5HHizUX4PziwhYDKUmazhXktCdpR8rNgHXVu/YztA4ovHeQx" +
            "0tuhyGlaS9/7E9j/alFT88LgHl1xsaqQprzeQJkMzJW4FUfGpnKcf+7mg5uyYIrhURCQ8dE4grQExLtRXu5sX4XUuTc61VI1rdeHZc5X0Zbg6cya7AhvEJy0" +
            "mKWBIgiCTeYsULUCnqIpAkp1O9V5LyatZs0Q7e8/YQo23vEb+FGd7vyvR4wla57Tdmb5WaO43CVyz12O41PPMByQ10Io5Rq8Pg0yj7k8ty51WeBiShKfbCRG" +
            "Xdk8LV2pbcc4DVYHZKNnB91m+v7LAzVPUp+lXLRN/qe5nLJMVlA6hOhgu98uB2KjV3b66RyNmedWQ411n+sZjarc4O9xQ1iZj5dAGaQqNgqNHusi5IKLHJ0m" +
            "5YY1hfkFjWdMf5aSo+jX3/B87Uww/XGMbChQSMSiHa3w2WtZh3Ctq8SeAQBNOSVhnrSvCq/S7PILPjd45K7mu2rUn3/lV4nMv5xETveXTX05xRunkRebDSzm" +
            "V9X9EBOOoA3tlEDDBHXRjs5qNapsGt8PQFrhJrUoa9zQTpgI2uuaKLDsbwapQxPbBbymSI5KfIanjXVFT9Byckj4nMK6cr09ZIeaUTksA3alNhQE6FUwx+PW" +
            "q7gcp1se2aGZfs/zT51p9EqwU+5Rbvkw9zepWMdLohPE0TBVZspfBdyPGy7+WqmucvRI6klDsLIRRzBhOOq2+cYndQe1RmAxktP9P2H3oxUyVaQHUZoQ/uBI" +
            "2XPqlku9S9E0K+uLw0L3VimndIfyuCVBA9ObMd46aD4UPD81PDwVHnpVLwJhy3hI/RZo3QQBxQ+RtsnHpqXBHgRAngFqbkJS/JB/L/lnUutT/lKR0AJ00hoR" +
            "gloeSh3JO4/5CvTW2c85Sa8klauBodeU8SNL2EymABmMIf9iLgEIF7wxctExx1rvfAVpyWIZpnOsvvpSrQ1yC6yu/cW5NdH9G5/0XV+i5YCL6YRIKxmZ8cVo" +
            "B31oaAzi76sxD3F6/NAwhuBjstDJdYSEPH2oganaYC3wbhz7gENzl8CzupkI9/traOAjz0vPopin6IqgyRqsuvyq4U2msaaYH/RHVMw0Bi1PgZem7Indl1pr" +
            "+H9K5fji9zGjgqZ2JVwF9qsF9u4wKcVwhNT9NZJCFz/0B8NIdUNXBjxoWU09xJrMCHhVCd22Mw0H9/oxlA0ZxRnpVqG1xIbhNQASdczlPENCQu2Ygbtlxt0F" +
            "psuvEQxNffC4+SXPcFBc7igtNwdjINNZGwFRc/DuOwpzAHP+mCj3Ltx76BSo9rEEd4fSYCYPXAQjA9l9Znt4asP95kt/+fIhOZbvql6jSavcHbrbAJLHMvVs" +
            "JcobXzLFmMxPQKPJx1pOjhprVnWROjbiP5h+lk+ewHjYCRSU0CR7dknvEV3q81Rd/E52fDI/ePjM8LsU22TsagHy9RIjk2vF+OccJPYnY3UOb9QDZGwqLP5x" +
            "pqi1YWVAhXKNN/9hEaJ1nZj9QU0wXBT1+VLQyMkny+MgyOMN5LerwfkRuWsb+dyl84Pau0AytMCQ3LQurnm1JGhJITJ5D1gvYFerEnOPqIqLyzQej3EJwesL" +
            "ll4/ugb/USom38O2MUcNm8hO9Lr/dTL7G2oWRhzAFzPeafDRH7ccX9eAi36wURsS1v+D8zaxLTquGiqEDV8DO4vj3fpcDTH7jNeCiBjuUUYX96gxRJbZ2kxs" +
            "M1PznP3822UjJjHsYXB/Cq1xGYCoRKqIOqYlx7CTQiIv3G3phB9TGEjPV07gsew+ZDn1HhCBmQ0oS+duG7YDyhWZOAeW+vaH2azsBydPdbaKvvKTC8vTQMMG" +
            "cmU2fxxwy/JWLINBwa9qk/pELGmkpdIoOHrCobR8Py2i3uzytgl5piVUDFqIUpyQftwJM93OH6zDoiQ+4PeNt7qYKjdxlHbx9YAMEJ93vpHzUlt5bLrtXJBC" +
            "/aW6vo5nKkvZEcLGd5JsDW9XODW6qO7/hFDUVmUpNztfASshTqfQnJaZcoev91Sa7I7pIq2sU6vvgVCyXU+adrqSzTjit1yWQfQbAv2p3ZBUNU/ISXXtY+v4" +
            "i97HJDKt7SeKxm6SC+kVO1pydKzJK9dXwWkEnzJ0Zq+3+wvrK/Wcc4sWTBUsdDW5dQ1mIBk+C4P2JQKmyKD5aZqhPCr2z200rBkjqsPA3jJvVTu9ZB0k8rhU" +
            "xUQ0JZ8vvyToUDgdCvUrZyj5dGqdH1rDp0O/eKsyS+xTRA8qhv5IQ8Jp83B8pJcczPtonHOpLGHotY3ePWsvJ0FtWtqBz2asI7E72k9QJmiW1hSRji08nz9T" +
            "OiGJVj0k51LwXMD9QY18dBVuFWyZ2SqRZcwcC5xPEjpui2UMexCp7+aFjLdTjTMonO2tsKaaFeN4BwmEuttuy0XwvPbw+NBllsIHoulZ5IfpBYi/5Fy81elV" +
            "r3g2ePa1JV1KFeYyUm8U6qG50qqc+2kAIcQSX3YsqMBD1Z5bUTIAhWI/+RdA3FNTa0QnKbEQHYMQPwzydYV2oBFOZdB2AyXB3yIQMdY/8QJiCJsSn4y0Kigj" +
            "UoSzPPPwWcl9uIT+rEcerpu34d8/5BdKEcP52prCR0pEZQq+jhWEFEXfV6eyVHJehk7J3N6y3Bb4DYArGO3/FXNRidUL11j/P9fq3hL7/cZTYZAYRMoE2gbP" +
            "AFQ3ClJmIJoL+do0bM2GvwRBWKmVFN75/C/HunVu/N2NL0ILdTgW2o+I/K1vcsWTwEo170Kk+qlO67dk00q2WH8SYIchgxDA/MW0SCQ2O/WofY1FUiZKDYyM" +
            "FBwyhFpqBHoDybTi8G6MNLxay8uTo7jp4LrmbWAPf/+QAAoAAgAAGzgAAf+Tx8Z0Elr+ssW1UCpf2GgLZgae8i7RmFiyLz98pRfBHlTHymwktINrigwgDJQK" +
            "hqLNWWPtFu+CzOA2NZz1rx7HxFAuoJRTjYNXiznzL4zAQAwJT1bcA8EUPhLgZQA03UJbAUJfIjy8phclKVnBzvEzwOCmXMMOHwGQJjZJlkEf2V34mGkvcapk" +
            "SRnEuDaFP8EkPgOAEhcWOCks8SySP4CXJpnQ84HXwqYVQIHpx3rRphrQlYBXVMfWkSS0CmfCWFWAR+3M9ZxCzxj2qUhshGYX26OmAI/DUKHOt4V6ojKZEMdY" +
            "jrsdXKsEU8bDcqlO2OGhLU4bR0YsVhUmNlBINKdcuFNAOxe8oE0/Wn3l3ziMKQQSSXgrRVN4WCwQqHBFDYbXWdxMYJD2Cblhv2tgsrkOaonHVIWtC0CIkMZl" +
            "3ESATkyX9gAswuGfnvuA8oVzvA4f30k39AhJQ2OhcwkBfdst68WEoC8LV73LDa0nEBRH/4yQx1hEYNgrLsoBv8LjriEj9DM6qARHl7La4a2fGa2rePhgFu8P" +
            "ovDCyummBcV09LCTWUv/SL1qy93uGPwm43DAwZ9dj8qJfwXdDRFTFtYMPEpLIJ1+xt0fUfxjfF9JL98UeONHpyb+6LyE4EUF2y6Jyu+9Cu+IIClMXFW63r1j" +
            "/CVwsDyAkgYkJxZbG2r5i6ZlCoqRFS8Ke7wRDJfASRrWxKWCt/jfTsPUAARu041mRwyMD/JRsumKKqGqSTPXE/XrvumY2dFndkGBpO20+z/uHi25rPoqIPaH" +
            "Hn5FFa6GGHIoTQKBBUw1Kllpdqr+25IanuX94EPbYtmR8+s3OPDPu6n8D3kMPPBqgfMxfpwijaG9z8FfvP39owYKlou+UjjC9uAJBqgLV8zlhtmfG2aT9vTg" +
            "tIEOrdscVxGbrYxFmrWhGXpvgUxGzKbiaXC4aJhU6DoNqbQt4hRoOWSJ7fmtcs1+Z2/D4KXrcPPghExJ4/GuGusUQExlQS2Vm4NPWiEQW94ofRVleP6x5yMs" +
            "4xqikZPZNV0PH4NRa0ASt53VHXuhCGkWkQkJKBgAu4fhGkcuc1R1SDxbAoXUDZ/dgk5bkxuUocKVIzbVBCn277edvYcdOMeJ5zaUp5ViNMUhGtyBn7LT+2LV" +
            "E9xRgzt7WDFc358s1G12WZb8XZGbSU659t3T/dI5UsIKKwzCqUmT/168ixkCPURbmnAnSzByjroN7LO0hT8XK9S0xyYXplNBv4ZVo3605qIKJqiQh5AxcEIV" +
            "jF6EtP3IMt0psJrXNAC/C2e40DLL2/zI8a4WxujmPpr6TYntoMPGjHnLZcLngkC0E5nyLYUQ4wHzy5HU/vOZuRo43vwa71Q6kFjeuM7sPcso0xkFrSSpFv5x" +
            "GWVa69P6R2BTkdiflEPZBKA6ft1MEnhTSlXT9nKY9jKj0oLSwSZa6SE+ZrHkCusZlYaMQ52MzWCKbQ/JcRXHejIF2iZ5HEWd178xff9++TR+k0Kn6WfoReDi" +
            "+ellPvPm1t8kCLP/QbgURJMM3qc4mZPqdFb5B6Vs9VLAlYcw0LMcTwdf3LA/45b774pbLpWrj/VUpzfjGon1WJdK5oUd6zqP1c+KwPLC7VSWVLmcluBJFwEE" +
            "jx0VCYj+DGulEYPZQ6p8mMEQeanYjMPUTO+NGHPWQS1oBy/YNJvDYDFW+ntnbsKf9vY+HQ/h1UA0zeXE265Jh/L2wdwr6kCXU3ST40bQC2aZdQ+jp+0dZxdB" +
            "AKMBYg9heG1jnOzKOi4U9vLROsjWs2H4zdfOyf87RwKGZAPtCYDdHW7pNQwRsOV1AWFJ8EpbBYby0pneNwuFk8BWSgM8XTjUyMQ4RTyiaN2DCWddzS5O59IO" +
            "rgbGGTL8X7AhTboLtSe9b0sGKXhWr4zg2OeGLcgkiiDyeR+4UK0hbOo36U6c2JLw0ycyBOICnHU9WaD+f9BTVjk/mFgu7ehPnPddHzM+HUv/H/PBnk/FBVL9" +
            "B8AclJzoo4f+INfM1s6KcfG9BJAN0rL0RgG3iu465zpv/UPgQj+lUwy9v0iu0tDRe+xzDKdc71u0SWetRzIu5QE7a/1D4KcWrmH6X28XnAlvYxAnhG5apEHP" +
            "shnfiR82dRY342n9B36iH6Do6ePoANjt8ujZ+8no/IEPkvVjtWwC65Bnvdjz8n883dXRHmbVljbDbdVIfaEh1qVPWsOgFtok9fJK8BHnEeMHUHr6aJ4mtKM7" +
            "ochrxsbmrssDIsSv4ndQser/f/0H/oPfQeASsF6rpxkSVQ1CYuXibmgyjrnIeDzuLwUguC91mcZ/RFSP208u16NieS/5mb3nqzmdQmgx6oWy9F3MapZnCRCU" +
            "ytgz/LL3x/bRbfIilIx72A2Vb9DGlGzI4gXN/Qf+og+g8Pcnt8tNp4cACAi3Q5wD69JrbGBnT91gjeu4al5dLsfDoLss/VLLhDJwO4Wate4/sxG/J6qVbnJ2" +
            "8p3VF5B9j0SLsAODEbf6D4bQs8xp5Ylvw83eJ4GnM6sIzGC5v/zuP0On0NwApsBVq89m82ASqHON/yspV+wUTTzu4nzidnG3OwFr11ykyrWYZNyS5JfhUMqX" +
            "jEiGqK6bW3/MPFIPFHyQJQ+4Ye1+nd5459DQQuvUuVJNR1YTKJxmwKq4C3XJsq5nrMBB9BZOA60+cMjKIZXosKgkf5J21z7xfZSOt8kjGP2TAqaO9HNjh6eY" +
            "1hDCJWumkc8g80+Mblg7MXRIvr66aU39U1nRQNAQTHFkkwR2Z7q6mHJlwLMoWqfl4/Efzdm0ToNjO+hYC4giWuiNRybTpmT/TZnc8+XCgViyZxu9FHF9sc2F" +
            "AC5/qKOzD0MeHYqfnQxaFLNII+Yr93tbBtU3QDEkgLHVLr+mCBH0UceX9uIbhe0tpLlL2FTmM/9oVmCWy6wXvKLL5K/wjxKe2pMeplc8bqoegvzqz1OnqMFe" +
            "VmYlLQUlxSd28EI4Ni8LCQKk1L78I2/81t+dv+i5AL25cYPE6hGGJgsRo7mGd1YViW1kivp40/BJ8tnNT6U69X/IQfJbjDQL9jh2tAUAzinhA1/ECEqhkQA8" +
            "sXFM05QuwzgzqIFx3ZW0lbjuK/C6DRXKmPeMsgz5I53qD9C9LqQLY5GuBuQkozmiTEdrUg27A7+JQ1aF7qnIakOvWA6cJwnTodlh2wvbWRSTMaXBwifqXq7G" +
            "uOlX13wnyuI1xdB487goe/mVjUgXlV8pLcgSG48e7WE8158Q7L2Ehu2uDWOjsEX7BhkERybiFZ4VCyVLRjW3+FGeJ7OteLfg4xYdPOEK0Et4Y7LqyrTK82n5" +
            "hdbW0emUHWFfnROY0Zo4kiCeJyDQx3dGoRjLVMPmfcz3B8bUViA09LW2Ed6fgToGqqL83zxOyj5fHWdxOVOb9qKv9fohQWHb+2keBYGUbe+pnZ2o+06Y6Uaz" +
            "w4EF+d387d9F1+i6gPhn9fvKErUz44syVdMYivX0NWZ/igrGDDabV1XWdb2VRXdOpHl1EuHUgc+jzJXt8p0aZyuUe85Xa1AVH8clRGU113iCAC47UC/BFguk" +
            "7KxrCzsWLSVB8bWROoCJ/OlCxSoAx5qhRKv+I66LXEdHpfMm9q4KBuS8UBz85+oGYZeN/V3TUGWLbCuFP/v7ZQ6NyB9d7blKFj3gHaJdHQ2xDJRpdVoCSxwb" +
            "GnahG2hHaJfJzBaegnxAtVcfdnfNM3lMnRY3+HvSHzfuqXIdrolmr5Uez4ceaPEm0EJnrJ6ZD9Yn/YTfNf4RbJmFXu9mPcxC0YLb3cT/Kjn2jf9tH70WLxSa" +
            "VfXWpfCRpMeD7p8t1OD0YYiaweYs3nYTwwIYThtHwKy4S2q5wA1Q5VDs4hmpJ6xN+hCwAM6dBUaU+goRbU0IevHl7BPpYRxZJdiiyMBNriG/KIPrtrB//Rtd" +
            "9G130bYglhuO0fQxZujuePdJSL46afn6wsmTMIlfT0mrquwNcAMWzEsuBQMys34xTqwbNO0bCETOAgHNdg/sPPs77/8UmTr0o24ZTax2KdNnjJSrtgt4rT7o" +
            "OK8khtinUGjy/EXEYuLjT28AHm/2e3N0eidnIt+YUJDo/Fz41zw/6bwTJuiXRR2U/Ib37sdvlKcjT8CLuuKKRwCetg30wMO99nQTMRT5vuN+mX29BduWXsZX" +
            "hyMt/26Fy9Y6dk0BpRXajHYNlpp/Cr6EUWQBZjJo5woONfcs2ZrwBQFa0Ng8IYbWeyKPs1UPRoWGAPg+/FmpuDO7dyEg6h0q0ILLK5YsK28Bm9AKor674mG2" +
            "fG0LtKWnEe8JFFWyglj5Z62rexRGlMshVRE5+PXr1fLiAI0itWSjyRFOp2fYx4mzq3s/AjBnlVHlFC4QKzuv6s/sA7P03CxPaKwg7DjqHrXaqx8e0Jz12IIV" +
            "8QERVIeL2GjoL9FSFsYt8gq+v8htg06BgJZaxwNP+XU+z51U2XLBaFFShHBgub+rj9lX8jQzeOBq7MnSEAzcQThqBmAB/Zoqz8kyv9NIFHjOXHuDoFnLouWk" +
            "1PXnFCxOBy2kTgTlbnKFFvr4nR4Snce+XpOWKvogV/Y3O5KsJnrd+/0xeteD6X6Qk6o1LFMuopKcy51i2lSyYM+XO3EcCStJp8X38fYwEy5FhDjshVqUG4J4" +
            "nEEEPLc4mpHfn+g0lsMRH3fSxPhSxUaaHxjdo6C3jGBEtoDADZMb/uvDcKcUa8QM6T54QdovD5B5/pdstbMrHVMAYa3c4s9rZdHe1ZIuHfFK63z3uBZW5/65" +
            "7v1itSPclhD/iOZxngSh5MhVtyE8Mds2/YbLOJoRF4wjytCTon7hbVe7+uhmt9egkqqJDCpB2+gqC7m64UE/r9nb+MfrIRM5LZIT5L6eYQzM1VcjTlT3jJcH" +
            "zjvP0Kdjcql4AshwCXGX8O4Vd7Aq/V0AIPS+38y2B8hGqCabUYCofmNAKIj0bAoV6RQcauV3jIBhJmTpWgN1Ekkus2FC0gXZ0uUHfLkOcO0+sXMbwMW0cK5G" +
            "DKEEdCTWhPavc9b8O+7ybnnJAHCEq1N2m6Q6F8k5W07Jh4CN5Qm9pUid5BKeGHpvWHV2OaInCJ8A8WKnmidKq81JfJEBkq12Oc8IOSHZeQ9sq3GmNrB+/2Bk" +
            "bNvCEhv7B9YTkKBZneL1IL53QyBoRt/ooH+Xj5+H7cr5p3MkHCFWNjuqwqXtd2KWg7HoNzfyiMh3Qu5llirBKnTotPAcn/ZboBxnT4Bvl/tbYPQQ5PhbXobv" +
            "YU1fTzy7K+vfJQyI9XdqFtFcMuePbEPiv/OYiW52Vt8XpyxVgj9fK/0Cif7y2Twp1N3FgWCZpjwm7aFW/wa+UwudCGdFmcSCRIKkgAy6LCyHErxCQDRK06Sa" +
            "L2jlwyfEPulyRef4Zdf1NwHIcF6Fut2U7KOz0t0I1mPR8Ekb9lXlIofuPM6X6j03VDl5DrFugYbbNjc97FWZVUJqsdQ1++bTX5/2Riy6Kg3W1LLnQqwLNilh" +
            "iUZX25V69mwMw1376k2bxXIawiprR7tSnWfw8ufDXM4yHgvjUDvCOxs/1APMn4B8KZ323toHyajQNsaWF6wqgMeTiLgz/B/v9+Y0FGYCa5/8tRKhR3T25aPE" +
            "bULC8/9UmL5Eno8hVMYL7TCr5xM6OwVh3jjLqG0AvF0HGfLKOBV//Nan6LYfRbinskn1jDDxrz1HpEBmCEQqyBzhffIIXgwFFphLAu5PbhHwkS8bocJIltAn" +
            "zKaXmB/Hvv6DvfU4ij7V09zUd3Lq96BPrxBfElblSYn0UuCmm4K4qUurAV18xZg1lHND/Wj7sQdIBsAYSpwvJc2297l2wyG20qDGe1dLY7YprjyfQLtpxw3O" +
            "xqeqOtkGQ9+mZetT/wr59sTzyXNo73OcQfMiW2x3dOpba9Ixm4MN2OKzNyJ2HqTJiBN41T5Yr6iHamwt5m+g2rZ9Cl4nQntIxlRc73xCe4NZIW9j1Q7MVxZL" +
            "mXzATjaGOSYv56iY8woLSkteQEzrVdk3l02Ez6OXDGxLSGYOeOsy7cc17VTLnunX1OMX8hvvP7mOQpBCGinPeXa8lJobjp6d3b7G6ThbYy0R0Shxhc1WRoYH" +
            "KrJgq47ALxTXDS0LfHivfzLhFrHZ5ZYSB7cxQUOvgr6zVDnpFLtU1VsLv4kHiA8QbHAWRvpngg4TcHLMN2d9pvV9iW/euWxKFfEBoXTspHl+F/6lZ9eZVdSG" +
            "pg+qfma6OC40sLEw+yEGwbHM5/sHv3+7hBMsPpGHF9MOO7muUigQVM4p/36PPLI4qeKrEseDSI84rxG8fYz3lqbuQ32qb31aBOlLMqNEw+tpg3e4AOBT0Nr8" +
            "VfG9LzuqemevQaJ+GXQHzTo5iBjziOfxWQDYZULLEeytUGXeqymUTNdRxYV9aJm1r/Gi/qtOAyH5ihP/Lr94J/jwgYlHdbzny6yjxd7vJxkinCqJPrCX5JfS" +
            "j0ZztNe8mw2zHTs+Z453a47hK6vbkoRwPePVG42gnSZ4kE4Y5jqR8cXK+EMeuRNnGncdYY076Imh52UgykCCEdA3hRsTAYsFEddHTeG7dUx3ZZBb149nnxDF" +
            "I6mCUgcoJLS5XP9xALtCZGvsfVu5f8SmloZbZjqOn6Qucn5gTJpIrSjyXqehLzKPbHcs02QQReD72sf/M5lz1tO1AGyjuCco+2Pjbffw+UWoeVQ6pmErT/e8" +
            "l3+B6Mt4V8yHbklHBSE+N6WyIsVIqDNjs5N6beS5RC0oD/qdBL7umMCVAcbSWnm1ptL2znixWobo2fKTIEFnIdhJVDBk3y3cLRLT9pGB1tye0OoP7XQAzsLY" +
            "IgEUZ+EzBpbk3nSRAfAzzF3PlMKXCEH+Qxl5b25s12AjrIwzvIdkikoiE95E9l3Q/plfX7Wj4j+HYWswtv9csQKx2j+KSApHGRTdjuWTHaRug4zY8cYyLntX" +
            "5PG2W+kKCHP6TiKYXMM0SGCcQku9REGm4FAn0cDeziypWdcYrQymt3x73b3L3F3tpgacedIuN5Ta/du72pqV2gP4CgPa2Gh6+ZhUwmLOkNjEmMozup06cngx" +
            "SwZAig4x5PdHz0zIoh46S+FbZI7j6BhAOJc7jqz5TDQkXCUCh+BctZYCcIZkw29je6FQk2AoIGWnpaCctdmzKtzIJn8P5myoIGTRk7FYbkOh+i/FUkcJFb51" +
            "nFQqe5PeJIwS9afEWm16desQ3B0fvGuAySMOy8X8w4DwKm3bY8Hd3pfvZ5q9lZuI11FIunFassnl/uheyoAm+bJsv3sP0BFlY2YdniUpPLEa85MUI1UkLg4Y" +
            "eJIIVBEGasy+JNkEc0ADd+tuS2KKoHpvCZzM/uELoCss24oGMYhpJMlTcIMLCd5TzW4792fU3CTIy5TBGIfNLXxAq+pABSR+Y3auTlkiV6kbfai//Ruh9N6v" +
            "oucAOzwWq40omhhCaZN515ejhxWNuupOSgJKjbhbkDL2UKIRHeywAKhuOdsaIqT21iVmHg1Zdw+KTZdoP3Cd1i9VnHIWvY6oc7TAVK2y1+IWAah+5IvZuOaX" +
            "qRFKZGgtj2rN23/VJ0DC0gPW+QtBkAxzDS47UF52LymbZF8NsqVTO7jvKQRwcAQc2IxmwzxFcRBqhfeaca6aVL+S09LH+ocpIMHGsNt2xcwA61vE0RSPwRl8" +
            "lPzlo27pAwpDWsy5MQnrCVf40VaaeQU1Fpi7htWvM9XCbpeWEHfe46UPYhh96wgnuFbtC5JZuAJFn+fEoKkcQD6HcfRywYbreKpVrKxJUUEVvM2T3cm+WbcO" +
            "KqYtUERoGTf/UvEHZ2JgDXEeUlfjqOlf9VlH8Hi6GQGgekk8Qngb33oMVZu5Q/1LIgHolvgD5hcmK+9xVow1zBLeWdCgm8fw/3NGbhI7R9Ib0Si69B8VE0vq" +
            "+KfbAGTArN1VBm+M9Jp76pE/nLDG7TCGJY8KUvD30JNsa/cT/E1xYvJqn2ryYRDlQmzW8bTLTgqZdzjwig3XXJEmtGP54A1o0WFSkxTVUrDfbrfTRsKu/NvH" +
            "UEsz63NZtwQoyEJ7BSWCyRL5T58Vm7/2SPtyAEFEKl4+x4wdk92wXWbQFA/EiTiTukEdm8JPscRNhEHlepl7f1NKAdUQGLN2mvZ361F001D44JdixBeM9wgl" +
            "KlCUcNagoYE4cCKeX+2eqnx8P7P/Fuama1T2/u2uoKgemlkACVlEBru4a/CFzzTKH2Mn7JokgPQqpbxQKEsOh/P/fVqxnjHmeJdWrWt6YhqyZu6aWaOIsnnm" +
            "BCoCtUFPT0iDFwRoQ2pKKNhjkPSSa6m26H9nZYX9clZ7Yz4cGZvgIDFriP6mtlFDL3Bx0jMCJZBo7VI4KfkBNOPvnrI+ITerYPsadKqliAFOmmsoIoRtlO/k" +
            "pTwTod1O74ojn5txo+5NVL/TFJTZ/3vkqksC3yIS7dkpyvsiixJXn/PdJWXZ/tZafcFT8wSV74VnvsskVA36w++LCdovab/J+kj83reeCDHORSd29uO7vBHF" +
            "v6q0IpntO15EQ1S9AI1SnV+fRPFGJAoJ7QK2LZIBtjHSLsP9CFzNLTk5HFVYpBYmvHvV0CvJSORKBrMv4s+O/NWjtv2tHHhvME/OTFPkIhU8vJKxj3+bTOm5" +
            "C/xI6QfWUOKQrSj+cVqZuCMmmt++z/qU6qpby/dewFEf31/DdB6ApZx8g0MpyY19XtVZYEBwU5Z/wQOuZogKSqXKEny7gRyh/WdwMkPoTwGdvVQjHmpebzwW" +
            "epi6zZSWmV8Tk+Dm6S4393mdoY8+351+2SeqLAhXpFYjI6v9kPJz5wCI8imQD6rhuyb5GWLN7Ff8Y1CiFQb2G/Kvh+TYtcsRc+KWcN80IFecFpPonnLMgixF" +
            "/Q0Z/BRZW/16nhlFoWtBXimnTK/9+A37cjxqnV+za/cVp2PNeZkJQAYUvMhryicxTS0WEdZUJu27+5mMLvtltL9TvLJVXGdFL3c1XIPgB5dNnooSkaWd++9c" +
            "ItuQ1WdlTG0gmknyEtBnakZ3n0Df9s8xFoIoOapiKUP2WkoWq1zfBZdtOVIViqjBn/0Spq59q2sLGni9B7rVE8ZOtz4zCgFSzw+BzX+ux52G5gLfIUwnNRRz" +
            "ZFgQS/hMCTej2MhA0i+SSbeMfedNx2DD4jVU8UHVVXdOyz5eNhXcfCdk5RosRxa9ktn1JefM+5SWDFC/JcUH0sPxjg/2z9CGFDpXO1GPgd6emiiW35p4VHkQ" +
            "BxSCehF81bEEfHt74yqsDBbG1Io75ufPbPJNv0+WovubQ1OtkxoOBh1wd5lw5B8uPw3+KMgdS2fJMd3pZxIAX7AIxQ6v/5AACgADAAAa8QAB/5PHxnwUKiKF" +
            "qnM69rWa2FVZA/QER66Tmd7m42My0nRRLwkIx8pgEg/Ir8PU64YXLyU6EY6JlCBxCKTEB9Gax8pgD9GRG3K/px6OlsgRqx8AdnVnP5TFhclRwRw+AeBOOSwN" +
            "YUz2Vx0AWhG29ZJ+AKDBwcPhOgTAOLm9TD+A9nbC92Cx8bjICLItvWAxjLLB0aHwmwdHgiiQH63MX2OHGXP7UynnZqV+tzkqHXbQDgHEcKmOlIql0wyZDlp+" +
            "JKLRIpMji7S+2yKAJeTHBx0oYljQjrQKyMgCgl9d+aODAJUXHMdfjq8dZMpxxmt4QIOijG3/HdlR5qcpk1bR9ZxVoIZAzEGlR3rLBS+W1qwHLb/8uQn8w3Ae" +
            "CvuU2aDmtcPHXPzQ2kc+B5jMIEZHShh0c13XS5gmuAHC1Y6vC0ypX/VO3jvu/Qr/Hqc/eGYymu7GNAvVz3GWLpgLbWgsriWpD1bqhwfu3iehxauaj8OKPRWJ" +
            "5spxV6CnNAmhqcfHWI6szE9tdpkb4iokfqVJmgW/TxZGQeRv5FIx2Uc/dgYuRQQTQHF8/x6areR/e5Sgd/wGAJVhdJiiWMNwDTueQ/TA7LYswKn/IZn2WKYg" +
            "8OCu64wXo/JB/EM8Z4oacbQWbsNjh1MPSsQoMh8R2hIPlD/BvqywzbOndg8BIph4/Cb0rxjdvXj3aFAMJSMlpw6sD8ZyOnSJAXFWInqcAqtOhfKatM8mOvSf" +
            "F9JAjR/fRYXQg+HXWUA8cm+yjUrrL4AptEbddLol0y78Z7przADhzLsUhVz0EM+qbGUsQtnTZDRK5h5O+mo93zDv1BM6e7e762D8bU9Risy18ploQo3jBhNb" +
            "WnoyKg8hPU3EDRRLZFCEtGdS16THdRhyFPoUNvw0PwUMPgpA9CBEdHw1l4gC08wrJoUHs0kie5X0wH4haV37LsNl5g+rLH/kwgzg6wtsFndxAQLuMO3HPf0R" +
            "EsZFopjMJPPAmRKshqS/kpCkMvx16y3FKYXVKtNNw44P24dxXoW5YVfmSFTC5cxSw856jh1YJ2qF/hXQVFSgGdc/cXuyshRP5uuPuysMLHfn56+C8UysYZX9" +
            "2sJCsJp3J/V+4tB1x9nU9yW81hSUzHX5pSXRGBDi8o7HYtSygF52iv3WsIkB+g5Q3fbf2g9uAH6GEPQYAD3l0ilaXy2X0C1+UfPLj18jVlx83TObu6BzEt8V" +
            "6kgkbpQbe3KqFVIMQihzmrd3xasTv1HG/1/aiybLBBaGOlsi4GpQU+NwGOm7qMf2cqBst5+OUcPbzokEWP5ubfJzjqwqm7S4Bm+W0UY5TBdnJnmJvF3XPb+D" +
            "MXuRo9ToRrZi2oxR2dYKB0CcQKlscrlHXwgZOabJ6sRMdvuwSCFhT8Rmtfx/Ut4Xksa19Tfaj5VAlbPm+IyMoLoKBS/s/VD3JYDU62ZBFHjem108S8MvMyMc" +
            "ckkdsUtW23Eb1msT7PnoL9K88jF6x63pnK3oQ0kl1VM2ixyX+tEB8kUtlVURa5nVDkDJPhN1Ejgd9KGRK1Mmq4Uzg11wNy7kqGaBQHoXJ8kwVQikERkbZDFQ" +
            "thPO3a9q5vdMRiXw17Tpi3JiN+vMHgn8P1QEcPCRQnxLsqOVNO8JiTkN2hVBFUcwCPMHVykOqWuTVZhL3JMwKYN5J2edvWaZemG5wvxSe84oZklsNxKYrij7" +
            "kCw75VggRLZyUMeZf6/T8i07KuBKF7Gb7KYb/zzq9vO3cPaipD0ALknbzuN0WESXQwVKXSX49u0OB30OHdwFfK6MclWnpj4w9FE8UUZKDXlZ6IT63sq0Kk02" +
            "vEh3uugSlw1vhXxDzcAOU0pOpPUnzilIr9sUoGKbUjKA1qDk3PZB+VNxRDDqJlC6j8Wk4G3cJXtKd/F78yvMCxkiKp6f1uUJLWuu3fW0X2arlIUOvnzfLx9J" +
            "/3SKJvHNQN9gsi9jL2jpQnL4++t+MvB/C0ILQ0NWdsxztbR5OCkWFOnR4I98Whl4qkuaxR95tan85kB7qcM1bByLuhBTAVALho+n1f9A1fk9jRq3/OYABCRe" +
            "gS/ueOS2c8LwvkiD35a0kmr2U/Cg/QfAyfSDx862qmRgmwaR4S+eiN7w+11iTRaIlChV0ka63/znfoPfOaAayUnZIEusoK6oEUqcKslvVxuSmJ8UqECo4DV1" +
            "f4GN6FMx2NyQEvg3X36VyZUB8+SSpUUkjRmHhqH3P2RF5tw+GdMvGVJ+YR/dWiA7/0Cs3wezXY62/Mc+g785wM/NBZWIAf86/x/5UFer81+UtBlupLOBBXWC" +
            "t38JU4r/S6polivFGl6KRn7v3pKakAMiugurIe19U5uiw5jk/zuQo5S7zMwpgaRqTGUj23Fi1Eojy+P9B75zX0HQFWqGj09xeqZwh2gCz1sa9XPkiTtBxRJs" +
            "dDHq6YJ/uwO3i4a2NluJFJfEn5yppv8N99MThfDeHoufXspOkn1AhfuALaWVjtagn1t6hVeTokYzcSBuH/zWv6HP57SAta+9g/GIhTIV5kYM51mvGjYqn1T4" +
            "P0wlLipUGGD62/DnKYMG59Qlpu5HlLRpbRUr4syk+kQ8YIcYwDtCJpOanYbasZOgam10aY3dV3kbEXlhlK3kzBnhc8/ArS7VJHjSBzUvZ3uiLTW8Un/u9Qxk" +
            "dFcWO18nL6zhamI3JAISLurDU2BrK7PyK+R+la63aj1zEaEOCTYAKxgz1XORd9DaI0kkGi4tAN4CuoiUbcmn2VCncgwlpcxH3e/F9n6Mdo12/2XJVeCPKgRJ" +
            "BoVOEDu1xqB1j0xMHBN0DEynKpsN7SWS+DLMYaF94zenLlb2Rs3Rwc+/Mj2iWKtRzlvPTjddepsvF0m8DmtRk6uSN7CoMGyCCtpVAYMqeinKiSJhV8RvhE5h" +
            "MmEc1+OJtGTZR2+YK5bmhwfgTGAqCsp1tTl1PR3cUuUzKr6b/M3fM1fO4qdWT69/tNNFUZNuJ+bylhl1TK6DZjpE58QFdDT01DdsmXt7EbO4Pw+j3hX3Ym7X" +
            "jiUhbXInStWIN+T1mPCvd9jLIwOuGFLpgz191MxLdOAflQN+3w7/WnVv9IV/9ma/e4urZ/AI3hpY0UFbW+Q/9dDK4oT3UGtWpIJmoW8D+v5bPajajhrjRxNo" +
            "UcHUxj6fx3pMGXcDX4MRuD3QYPc19O1U3csgxvmE0FPMAQ36e1b2zmJ6SgrB6jDNmrVS5wg0SLzF/h0EdCDIHqITKsVMqavJnl08jf9lAkhKQV1zsaMsGJMq" +
            "BRO8FJWbHrqCbbLuZA40s4+kWOulXLZlP4tsQI/Ku36jcQzl+UoUhANsv+xMLU/dP8jXHYTEJ8NbghT0RoMtbYhvYzBhoT2uyxcqt2lcpf8VQIfLOy3Dx/Xj" +
            "AZlaKHokqvrn0De//Rdvoc/pvoD8Wf1LqnuoW8B0x2srDKaKi2AnGp4//nzhmeK58XHrgLtTGbQXMAxEF1zDGZcNIpyScR+WG/8dxNxnc50W0ZHGZAsivW5W" +
            "iU5mafQoFUQSeFsYl6QJI315DHGIo9Q7iteiNJeHwT2kpTxKoiDs/I+OMMylrcm/uimNmKv1KQ6PfmPzU+zR5mTpyOGmbVMBYHTvzAAWV65NPp3SI8nlwtVb" +
            "Wu2DUMf/bYhtJqOPX2a8papHfjD8U4TV/bV2qaqM9JbfjcIYVIFdlL8TuS4YP7/AQWMPQ9m+cEdLsSmUX2YRnF2tV2fsnnh9V+DhWUGsGBBStbJ8/20pSdfb" +
            "WWjuFwcsLKt+5VmOsjL4j1uaU+fd9x8okV6PgIV0lqxBLbjFRHiZqirgRE45g5l9QxMzlAeuW56M0tb/ajK0dINKy3/IRpjKDgaRW9yNCNOf8BT818S+7VSV" +
            "qxmEVbg+hQjZXk4nJYNFLr0//RtV89o/o2sAZtbfD1kwGOTkVd+ehb8uY20qIsDJjqXVOzEg3GJREB5GhaQ61oFnb/kHmp9S6t9U1sC56BwPVSVjysbiKJRZ" +
            "lB/+L/emEDPMJlFfo0cA/0KMrGs3DYsmhi7SYKukcIMnYN2uGVD2KIAH9zyjmiSSuqzR/InjqpvqqWFlUVpjiCtigp7JTEyaYw8TR8DyFgwPZV0KP2Dk7ohh" +
            "nyPVU2DObEuJ7kCbZgFTWxuDRaK/+iTDfQKXuneO+1HA9x/WDbKMSK/mDbwYSceFVfqRsmNVAJpVnFsvIV6Df0SrUZTHBJd+5im+z649MBl+HNwuAvrxT5Qu" +
            "bqnH2i5dDJVjDYH2c1EXta/QfUyRTOIJqAZ90hOBu5GN12/oZGOhyXxlYvm0wCweZ72N/2HSDZPLM9VlpKe8/Y6FAsuChWa0OtclVW0Fh8xAM9mPZnSOG76T" +
            "5uWjM2cCoWMUNe7hLZE/GMxnRZvcmqaOhcxEHjHjVW0IoWF/bDnkksMkJ6gQz+OAAtgGRe9X4KroK9WixipkM2bYDV1mA88FH9j94q5H2pQJpFFf781wxPjs" +
            "8cwvhkPDReUy3tfsBCAGKRx/5ppxJcYDB/hrD65i4WUVzSH3ZzXrlSrQXmTS96d2vrFRt5MGhcQM1f3vQahC5eUkde7Yle48J+f3WzRLreSrx+kkOeIf1+p1" +
            "1l5SDvhFMOPYrzDbsow1JxX0jHIOwTHx8oHWnTTaiYLTa8YTBqXZQC3Lcr5b8PEof7YQm3hLbA9pf12e041yy8YeMUTUa3mqJusL62ztFh2ZJ5tNVAR/wEFw" +
            "oy3tbNSve/AdXcKniZP7PByPDqm3i1tiL8nzHvj9JykpzJMFJY9KvogcoBjl1btj58E0IzO+GErUXBb3g/N4vh+HAUb2WUg0k4ZauZyHOCQzGO2JOkeykOMg" +
            "iL5lB+ki2ATyROD7bCSPQJ4AaKoksgZ7oLgWGQUYoxUcpNjHxkTQ8J0j6Y1rjO5f0M4lnQYrNvOEIEKGI+AJTfbRJGmLPOmLhy041UaXKQsTts5ybVxg3lvd" +
            "Hhj0NHmXCpTTTaX/ClKoosmPr0PvpaTWWPCpnom65mkay9yaXSeeKcseMembmDuZvSKGz1oVqxq519cxY99hqH6oSsfg477nR3MVeCqgLN14bHucK8CN7bm7" +
            "zWBrD9PIE9FAxIYoncdOIfZtd5JoCanmzYeAyEMfsRu884MtA8+tq4Cuus3YqhRy0EqDuNkh5Vuobrft1zM+uGApRD3otG6iXeyAaiDJjtGOZbNE/YIs+sYi" +
            "Weal/atv/YunxdtsVKvGma5mWaVwcGFpbEqFYegVmKIoK8+D6P0x2lSKAH3+MAj2PYSG5zNT4fFmYWkFv3q16NNZPCIshIwl84V7hnRYiEE1eArio9eDzIpK" +
            "tzpWZzVzjA6k8nS9YObDgPlNmjydGrQ7SqvuEu4K6wdk8y2DsDkibyD8vmbpSuzVhfSOdXnMQPQP2G2L/FIGmpWiDWlUX6kjiJkaLpSEH1VrAaO11Cdg4ENg" +
            "9RPlnRv2/WbYJTeS686J9mWPyg9u43Hi6Lip8HYjJEX/NtFHiFXVT8n4dZd5FIEtDaRlwANaJWVlYNSbTFHV8/h758q5wWtNajhhOanrVkSKA1hqIMkLh/5S" +
            "BtDXLwVzIjj+KaAcu5IQQ9pkkwNnwWp9brfbY8596Dpdp6u5BrUJP/0W0+e2Hz2vBj/fEcSquMdHOFRB1tvTCnKAykjjdEMaYG3DcY3Nu9kIkaxTDjnRvc/O" +
            "pz1AcBBLkA7VL5DPHCLHcH2O4+k03XQkrdO5By2r5CrrZQiy+HUclRRkc8SQs0UEf9NLHu5VEvVjzDYPv7AklIpctnDpFxF9W2k5Njb39glE2AtYJRi9Nmab" +
            "VOUxmSA88uSHy3XjkjoDHRN4CHlUbH6aMOY9jnzB4u2hUvz209MAq9WVr0xYFdtYd1V+N0n6YoIH/1PWox0rsSWQD6DrwSIhVTiyB52u56PFvjLbT4P8SLtG" +
            "iV24rPZyWF2GMX1iK+mxP5cepsozjpheUvuRV8flD5xI8LTwNR1QR/Wcq7Y+2tJIf5S5EjcZGbGdfB9Lbdgkd2K2EeGk5M/sHZVAkOQMIZtv2tmmIARCGonS" +
            "s/wm48yRAaYpzhWe0npLr4NKMehzQXErcHPTwuuASVrqqXvEP1poB1pqe71/WZyRV+lBxM7vMmfadJ761wfZMXW7VwjARPp9u1WsmciAUya3+Rt7EHQAhPFf" +
            "NDcd9zHWwzmFbaXR8Df2X5jU+AvYoGeB7dm4tdVsGR6K30+DcCSqu/IAsvbODe/CK1pDC3gOjl4DIq11FjM3UdMtMWiMSgo9570MbR03DczTolmC8r3eTdpN" +
            "BD7SBmic95TBq2CpXr2PhVbubPje5V4GIqAw8vZ6mIG2JUd/3f46+BbAP338IusHOGJ/oSCMR3Lmc7Ng4rw6KxxAM9+tV7k4LUlgpVk0BzwghSGaeNty5p60" +
            "Zxyx2aYAJaCsMsU2lzq20oOPv+8ScYoHVBdwx+fFi5ShMnC+XwlqUPYOIIjiKJctA9cQyr4LzSKAGt1xgSaShGPAkJFlPIXRahXalfWqhj01oQrDEFnZj0Q+" +
            "gRfRr+cSk25A3IuYjZLgMHvpGeFbeUaX126m5F6Wx5pCefsAacMDc/XTHPXWfwbEyd8sGzsssd+W4SWIPpkSV+u+TbIC9ulFUllbvaA/1y0g/kEhYu2q9hi6" +
            "Vk3tIhISNA7hF4OZ5kHplgO8dFx4QmhyXq9sj3/gM/iZnqCG+NcVHPy1T4InftjUhponfz3d0x1oU/KPvKwoJStZuJ0ZK55ol3ep364Kz9MUcvVxIACyJkCM" +
            "G2oyGygxwMKaRxo/LRMJLwtBKSr++3KphSbDHcWOPsDgTQjBRQPadSF9Se5T74qDjPOuq9HT1N2d48cxgxL2Fvcbw0xqswum/cNKJjFva84iu70z9I4aXRvL" +
            "6AZVZYQwdWmYRKAYO/mNubiYxLQG3QTfM9cmAW9Tzty+cMQejItwRsNRMBQazAER1UP8f1bZHx+tY8p5KAFNpMFHmH1c/SXrYwlHaayj0o6zG/9AJ7pTAbWp" +
            "7s/ub1GsvLdbWafuSYC4EAteqTr2UvvT4uvlQ6YZY1uTZPOFv7jxFXtGQrjnBcWF8gDy3k1RRyLFts8ATpOImLoGyXjrupB+wab1xyQajgzXzm1ei7LX46On" +
            "E+Irlex9tGWXZTP2H/X2otMtqTV+kZ4Yv38SNroCsq2rke1BWW2FrgVZlb0iTsHghh0X8N94cD7CLei23NYbPDa/wmscC9QAJtWMzCDolSMRHGRVZX+1e/X1" +
            "VebOHw6yagloC4+KVd4qP5XWFYxl082YPIb2Z0RbdCiRgrMaczSGLPJckF3+8SIhs46iQ/3laJExc3X+3KeI4KMkMUO+LvMxFC+T3W8fNTZvRX3zpkKn/Rud" +
            "9G6H0XGAy81zHmuZvwnXQNfLoVrBKJT89L+57Up4SE5YgqDGxz3f4xDtJyETbtP9JgV5cCaCWc2u07k3nyzd8lz4HMaTocGLQLuYnVZoQJUJjSwK10czjt+5" +
            "q9NGDfgZbknh5J4ZNas+or3+fBSKP1+sOIEqT6ce4BX6TUhMq8MVksGzTWXZQ0wyz9uTDgRHphoBBF85AKOk8i6g84EQWRlvtiBoGALcGjXwfWZ/W2n5s1eG" +
            "wE8KXK4WpICnLQeGHJYbKa9BeLYciKq150lmeXOl1eVDbPzuRMOvYTEdBOk2f+qwbJfShtNBGgpgo9hbmOxqJV2vHpsEsgyZ+tbUdOBGX5y/XgT5m9K83GI3" +
            "WVqZXceJ2f8rhR++lhps6HvTnFFocpnKx/4mwRoP9wH3HO/0jeL9ya3Pw198InKZy1H4fEbFsuC8Jyc5XBtxbGF6pdxkEsgiW1IEwevA+Y6wqLMKfqrSLLBO" +
            "OM/lmsoTzsBgqVznvftiBu6czmP12wgb/1DmrwjkTJlBCLTR9e3HsKebzyiyHZkecRvO6iiYb7G5fCJ0FnRwJYbjTZDKwz9DBtiKDaRpjhoZa1W1ErnO8rta" +
            "YaZBKqZmBENq1qAYd5t4PFFLBuO54GGG29Je9AHHIPRfKIeCaG07TY2t6dtqyOasOli2Iu9uXd9BNMVOoZawJBgyyBV437uujJ2pW8Pd5HR5zmKSrmPkXl9e" +
            "rFmwIWRVfuZribhqllrvNINpWJqfwcWdZ/6BH2K4wfbbomXBjeFRRkmuckqT232j5vvJyIMXCujeTZ2XQUoK48l0M2CZ0OCrQmXYqy6D1s8wLdCdi4C3lOLK" +
            "Zq4ErJYmyC3e3YocsDj3sXBZ2Va6T1cAuxPP3tNwEhVolabzaHZYvZ79On7nNi+1WQyywm2DzkvBnbJEpCYWrqoxRdRBBTM+gfAOL8xulSR9bEXhZxoLbo14" +
            "OfP1j4fz37EjEH1punaQuJUXJPFeF0MfrlLMppg3I+VM/RPxTL2Ei/epg8z0YVwbAdW10qI2wa01yx09KUDWSNZz/ueMkYqsQ+4NUN0ru5pLyFBHHM1d5F3z" +
            "dcJZi/LFyv8YzabPX5u/KDVUV4vG+gjH6rNkXJQxvpmC8ZWL8I/tnULF9u6Ww3ScKdmfX2lLqUNQf2hUmj6kvWu+PlDRZc/t1+s1+Q6EIG8nzMHjHhsGGsWB" +
            "9lFtOM6nKgxUVEqfGOGZgC8l+p3Xyf6GveIGT/Lo5N8cFbab1W+pI6YhjElARDQaWcJ3Wr/Ot36Ji0NnqFwgXSooG+I0hUh5QAjjEY65sHyfkpSH62hZoQ17" +
            "xzUmdaayuVoZvyqRA1oCULoZYDhGxa1e9/rm8wWMQcmn1Lh/Rbl/fcLDiATg/Y/nOsuyQeOAi+5YDgfvTPIG3/K+Tfegqb0scvCNJhnpjDsdiOsWfLqb3XVX" +
            "LBEVD2nQ1RPk1RmC8JOhJMjkjGul/bCfa3jWYYjfcn0SEwwbaU/V327Y7N4Fr8aRyWOWLjkyGxAN8HX0zRFuNjZzuUnCtfiZRpBKapKxxqCS2E+7KStd4NxF" +
            "VB5gc4Y5AXvyaFinFH+wv+WMZ3SNsKoxDixwjGKFui2eLnz61SHz1lpbEg6afW2sf0O9b1JKB65S92eUYEH8ImxeMdfzTBkKvWixJBlh/dx+SefKVKtbn6ba" +
            "gP4lUbt/IQ80Cgb9kj8Q6Ir8b6yeVAb1RHsq9H06Kz62I6x4/1pNFDxVibNxcxYl1zzRb7akRVErwtpokbCU9XvIkgiBrrXFvk0iC67DboPuxg1LjnVlghyd" +
            "0uxyJjPCinUtBc8eLcK4m1TJMwPxdSQ1HZtS1/XMsaQbmwIh/9k="),
    ];

    /// <summary>Returns the vector named <paramref name="name"/>.</summary>
    public static JpxVector Vector(string name) => Vectors.Single(vector => vector.Name == name);

    /// <summary>
    /// The deterministic source image of every OpenJPEG-encoded vector: component <paramref name="c"/> of the sample at
    /// (<paramref name="x"/>, <paramref name="y"/>) with <paramref name="depth"/> bits. Mixes gradients with a short-period pattern so
    /// every coding pass and both wavelet filters see non-trivial data.
    /// </summary>
    public static int Sample(int x, int y, int c, int depth)
    {
        int value = (x * 3) + (y * 5) + (c * 40) + (((x * x) + (3 * y * y) + (7 * x * y) + (11 * c)) % 23);
        if (depth > 8)
        {
            value = (value * 251) + (x * y);
        }

        return value & ((1 << depth) - 1);
    }
}

/// <summary>A JPEG 2000 codestream encoded losslessly from <see cref="JpxSamples.Sample"/>, and the image it holds.</summary>
/// <param name="Name">The vector's name.</param>
/// <param name="Width">The image width.</param>
/// <param name="Height">The image height.</param>
/// <param name="Components">The number of components.</param>
/// <param name="Depth">The precision of every component.</param>
/// <param name="IsSigned">Whether the components are signed (the source then holds <see cref="JpxSamples.Sample"/> - 2^(Depth-1)).</param>
/// <param name="Command">The command that produced it.</param>
/// <param name="Base64">The codestream or JP2 file, in base 64.</param>
public sealed record JpxVector(string Name, int Width, int Height, int Components, int Depth, bool IsSigned, string Command, string Base64)
{
    /// <summary>Gets the codestream or file bytes.</summary>
    public byte[] Data => Convert.FromBase64String(Base64);

    /// <summary>
    /// Gets <c>opj_decompress</c> 2.5.4's output for a lossy (9/7) vector, interleaved 8-bit samples in base 64, or <see langword="null"/>
    /// for a lossless one (whose expected samples are <see cref="Expected"/>).
    /// </summary>
    public string? Reference { get; init; }

    /// <summary>Gets a value indicating whether the vector is lossless (its samples are the source's).</summary>
    public bool IsLossless => !Command.Contains(" -I", StringComparison.Ordinal);

    /// <summary>OpenJPEG's samples for a lossy vector, row by row, component by component.</summary>
    public int[] ReferenceSamples() => [.. Convert.FromBase64String(Reference ?? throw new InvalidOperationException($"{Name} has no reference.")).Select(b => (int)b)];

    /// <summary>The image's unsigned samples, row by row, component by component (what a PDF reader delivers).</summary>
    public int[] Expected()
    {
        int[] values = new int[Width * Height * Components];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                for (int c = 0; c < Components; c++)
                {
                    values[(((y * Width) + x) * Components) + c] = JpxSamples.Sample(x, y, c, Depth);
                }
            }
        }

        return values;
    }

    /// <inheritdoc/>
    public override string ToString() => Name;
}
