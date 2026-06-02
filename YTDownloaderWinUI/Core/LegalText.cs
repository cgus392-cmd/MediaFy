namespace YTDownloader.Core;

/// <summary>Texto del descargo de responsabilidad y términos de uso (ES + EN).</summary>
public static class LegalText
{
    public const string Version = "1.0 · 2026";

    /// <summary>Secciones en español: (título, cuerpo).</summary>
    public static readonly (string H, string B)[] Es =
    {
        ("1. Naturaleza de la herramienta",
         "MediaFy by CG es una herramienta técnica de código abierto que utiliza software libre de terceros " +
         "(yt-dlp y ffmpeg) para procesar y convertir enlaces multimedia que el usuario proporciona. MediaFy " +
         "no aloja, no provee, no indexa ni distribuye ningún contenido por sí misma."),

        ("2. Uso responsable",
         "El usuario es el único y exclusivo responsable del uso que da a esta herramienta. Debes cumplir las " +
         "leyes de tu país y los Términos de Servicio de cada plataforma. Descarga únicamente contenido sobre el " +
         "que tengas derechos: contenido propio, de dominio público, con licencia Creative Commons, o cuando " +
         "cuentes con autorización expresa del titular."),

        ("3. Usos prohibidos",
         "Queda prohibido usar MediaFy para infringir derechos de autor, para la distribución no autorizada de " +
         "obras protegidas, o para eludir medidas tecnológicas de protección (DRM). MediaFy no elude DRM."),

        ("4. Software de terceros",
         "yt-dlp y ffmpeg se distribuyen bajo sus propias licencias y son propiedad de sus respectivos autores. " +
         "MediaFy no se atribuye su autoría y agradece a sus comunidades."),

        ("5. Sin garantía",
         "El software se ofrece \"TAL CUAL\", sin garantías de ningún tipo, expresas o implícitas. El uso de la " +
         "herramienta es bajo tu propio riesgo."),

        ("6. Limitación de responsabilidad",
         "CG LABS y el autor (Camilo G.) no se hacen responsables del uso indebido de la herramienta ni de " +
         "cualquier daño, reclamación o consecuencia legal derivada del uso que el usuario haga de la misma."),

        ("7. Aceptación",
         "Al instalar y usar MediaFy declaras haber leído, entendido y aceptado estos términos y este descargo " +
         "de responsabilidad. Si no estás de acuerdo, no uses la herramienta."),
    };

    /// <summary>Sections in English: (heading, body).</summary>
    public static readonly (string H, string B)[] En =
    {
        ("1. Nature of the tool",
         "MediaFy by CG is an open-source technical tool that uses third-party free software (yt-dlp and ffmpeg) " +
         "to process and convert media links provided by the user. MediaFy does not host, provide, index or " +
         "distribute any content by itself."),

        ("2. Responsible use",
         "The user is solely and exclusively responsible for how this tool is used. You must comply with the laws " +
         "of your country and the Terms of Service of each platform. Only download content you have the rights to: " +
         "your own content, public domain, Creative Commons licensed, or with the express authorization of the " +
         "rights holder."),

        ("3. Prohibited uses",
         "It is forbidden to use MediaFy to infringe copyright, for the unauthorized distribution of protected " +
         "works, or to circumvent technological protection measures (DRM). MediaFy does not circumvent DRM."),

        ("4. Third-party software",
         "yt-dlp and ffmpeg are distributed under their own licenses and are owned by their respective authors. " +
         "MediaFy does not claim authorship of them and thanks their communities."),

        ("5. No warranty",
         "The software is provided \"AS IS\", without warranties of any kind, express or implied. Use of the tool " +
         "is at your own risk."),

        ("6. Limitation of liability",
         "CG LABS and the author (Camilo G.) are not responsible for any misuse of the tool, nor for any damage, " +
         "claim or legal consequence arising from the user's use of it."),

        ("7. Acceptance",
         "By installing and using MediaFy you declare that you have read, understood and accepted these terms and " +
         "this disclaimer. If you do not agree, do not use the tool."),
    };
}
