document.querySelectorAll('.item-library-thumbnail img').forEach((image) => {
    const showPlaceholder = () => { image.hidden = true; };
    image.addEventListener('error', showPlaceholder);
    if (image.complete && image.naturalWidth === 0) showPlaceholder();
});
