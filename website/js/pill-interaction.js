(function () {
  var pill = document.getElementById("beats-pill");
  if (!pill || window.matchMedia("(max-width: 900px)").matches) return;

  var hoverControls = document.getElementById("pill-hover-controls");
  var loopBtn = document.getElementById("pill-loop-btn");
  var playBtn = document.getElementById("pill-play-btn");
  var gearBtn = document.getElementById("pill-gear-btn");
  var collapseTimer = null;
  var collapseMs = 120;
  var isPlaying = true;
  var isLooping = false;
  var dragging = false;

  var pausePath = "M6 5a1 1 0 0 1 1-1h2a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H7a1 1 0 0 1-1-1V5zm8 0a1 1 0 0 1 1-1h2a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1h-2a1 1 0 0 1-1-1V5z";
  var playPath = "M8 5v14l11-7z";

  function setExpanded(expanded) {
    pill.classList.toggle("is-expanded", expanded);
    var wrap = document.getElementById("desktop-widget-wrap");
    if (wrap) {
      wrap.classList.toggle("is-pill-expanded", expanded);
    }
    if (hoverControls) {
      hoverControls.setAttribute("aria-hidden", expanded ? "false" : "true");
    }
  }

  function clearCollapseTimer() {
    if (!collapseTimer) return;
    window.clearTimeout(collapseTimer);
    collapseTimer = null;
  }

  function onEnter() {
    if (dragging) return;
    clearCollapseTimer();
    setExpanded(true);
  }

  function onLeave() {
    if (dragging) return;
    clearCollapseTimer();
    collapseTimer = window.setTimeout(function () {
      collapseTimer = null;
      setExpanded(false);
    }, collapseMs);
  }

  pill.addEventListener("mouseenter", onEnter);
  pill.addEventListener("mouseleave", onLeave);
  pill.addEventListener("focusin", onEnter);
  pill.addEventListener("focusout", function (e) {
    if (pill.contains(e.relatedTarget)) return;
    onLeave();
  });

  document.getElementById("pill-drag-handle")?.addEventListener("pointerdown", function () {
    dragging = true;
    clearCollapseTimer();
    setExpanded(true);
  });

  document.addEventListener("pointerup", function () {
    if (!dragging) return;
    dragging = false;
    if (pill.matches(":hover")) {
      onEnter();
    } else {
      onLeave();
    }
  });

  if (playBtn) {
    var playIcon = playBtn.querySelector("path");
    playBtn.addEventListener("click", function (e) {
      e.stopPropagation();
      isPlaying = !isPlaying;
      if (playIcon) {
        playIcon.setAttribute("d", isPlaying ? pausePath : playPath);
      }
      playBtn.setAttribute("aria-label", isPlaying ? "Pause" : "Play");
    });
  }

  if (loopBtn) {
    loopBtn.addEventListener("click", function (e) {
      e.stopPropagation();
      isLooping = !isLooping;
      loopBtn.classList.toggle("is-active", isLooping);
      loopBtn.setAttribute("aria-pressed", isLooping ? "true" : "false");
      loopBtn.setAttribute(
        "aria-label",
        isLooping ? "Loop current song (on)" : "Loop current song (off)"
      );
    });
  }

  if (gearBtn) {
    gearBtn.addEventListener("click", function (e) {
      e.stopPropagation();
      window.open("help.html", "_blank", "noopener");
    });
  }
})();
