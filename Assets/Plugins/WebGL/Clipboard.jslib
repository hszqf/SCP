mergeInto(LibraryManager.library, {
  SCP_CopyToClipboard: function (strPtr) {
    try {
      var text = UTF8ToString(strPtr);
      if (navigator && navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text);
        return 1;
      }
      var ta = document.createElement("textarea");
      ta.value = text;
      document.body.appendChild(ta);
      ta.focus();
      ta.select();
      var ok = document.execCommand("copy");
      document.body.removeChild(ta);
      return ok ? 1 : 0;
    } catch (e) {
      return 0;
    }
  }
});
