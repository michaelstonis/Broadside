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
        new("Irreversible", 8, 8, 1, 8, false, "opj_compress -i Irreversible.pgm -o Irreversible.j2k -n 2 -I",
            "/0//UQApAAAAAAAIAAAACAAAAAAAAAAAAAAACAAAAAgAAAAAAAAAAAABBwEB/1IADAAAAAEAAQQEAAD/XAALQkgkV9NX01di/2QAJQABQ3JlYXRlZCBieSBP" +
            "cGVuSlBFRyB2ZXJzaW9uIDIuNS40/5AACgAAAAAATgAB/5PPwEQRUE7NRDV1j5A2UocjIdd/H8B8guA+QZA+cWAMj7WgTAevrXHQHxeGNkf2JnJZEp3JDx93" +
            "/Ae3BbOmFZqf/9k="),
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
