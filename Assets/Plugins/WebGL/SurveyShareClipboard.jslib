// Survey share URL clipboard for Unity WebGL on HTTPS (GitHub Pages).
// ES5 style is used for Unity's Emscripten .jslib compatibility.
mergeInto(LibraryManager.library, {
  CopySurveyLinkWebGL: function (urlPtr) {
    var text = UTF8ToString(urlPtr);

    function showToast(message, good) {
      var id = "survey-share-clipboard-toast";
      var el = document.getElementById(id);
      if (!el) {
        el = document.createElement("div");
        el.id = id;
        el.setAttribute("role", "status");
        el.style.position = "fixed";
        el.style.left = "50%";
        el.style.bottom = "20px";
        el.style.transform = "translateX(-50%)";
        el.style.maxWidth = "min(90vw, 650px)";
        el.style.boxSizing = "border-box";
        el.style.whiteSpace = "pre-wrap";
        el.style.wordBreak = "break-word";
        el.style.direction = "rtl";
        el.style.textAlign = "center";
        el.style.borderRadius = "10px";
        el.style.padding = "12px 18px";
        el.style.font = "16px Tahoma, Arial, sans-serif";
        el.style.zIndex = "2147483647";
        el.style.boxShadow = "0 6px 25px rgba(0,0,0,.35)";
        document.body.appendChild(el);
      }
      el.textContent = message;
      el.style.background = good ? "#155f43" : "#632c2c";
      el.style.color = "#fff";
      el.style.display = "block";
      if (el._hideTimer) clearTimeout(el._hideTimer);
      el._hideTimer = setTimeout(function () { el.style.display = "none"; }, good ? 3500 : 10000);
    }

    function legacyCopy() {
      var field = document.createElement("textarea");
      field.value = text;
      field.setAttribute("readonly", "");
      field.style.position = "fixed";
      field.style.top = "0";
      field.style.left = "0";
      field.style.width = "1px";
      field.style.height = "1px";
      field.style.opacity = "0";
      document.body.appendChild(field);
      field.focus();
      field.select();
      field.setSelectionRange(0, text.length);
      var copied = false;
      try { copied = document.execCommand("copy"); }
      catch (e) { copied = false; }
      document.body.removeChild(field);
      return copied;
    }

    if (navigator.clipboard && typeof navigator.clipboard.writeText === "function" && window.isSecureContext) {
      navigator.clipboard.writeText(text).then(function () {
        showToast("لینک نظرسنجی کپی شد.", true);
      }, function () {
        if (legacyCopy()) showToast("لینک نظرسنجی کپی شد.", true);
        else showToast("کپی خودکار ممکن نشد. لطفاً لینک را دستی کپی کنید:\n" + text, false);
      });
    } else if (legacyCopy()) {
      showToast("لینک نظرسنجی کپی شد.", true);
    } else {
      showToast("کپی خودکار ممکن نشد. لطفاً لینک را دستی کپی کنید:\n" + text, false);
    }
  }
});
