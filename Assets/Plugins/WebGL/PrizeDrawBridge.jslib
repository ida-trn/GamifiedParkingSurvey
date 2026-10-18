// Unity WebGL bridge for optional survey prize draw session linking.
// Keep this file in Assets/Plugins/WebGL/ before rebuilding WebGL.
mergeInto(LibraryManager.library, {
  PrizeDrawLinkUnitySession: function (sessionPtr, participantPtr, buildPtr) {
    try {
      var sessionId = UTF8ToString(sessionPtr);
      var participantId = UTF8ToString(participantPtr);
      var buildVersion = UTF8ToString(buildPtr);
      if (typeof window !== "undefined" &&
          typeof window.PrizeDrawLinkUnitySession === "function") {
        window.PrizeDrawLinkUnitySession(sessionId, participantId, buildVersion);
      }
    } catch (error) {
      console.warn("[PrizeDraw] Unity-to-web bridge unavailable.");
    }
  }
});
