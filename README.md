<div dir="rtl">

# Strata

[English](https://github.com/carthageadev/strata/blob/unity/README.md) · [الكود المصدري — فرع unity](https://github.com/carthageadev/strata/tree/unity)

![عرض لأداة Strata](.github/banner.gif)

[![تنزيل حزمة Unity](https://img.shields.io/badge/Download-.unitypackage-2ea44f?style=for-the-badge)](https://github.com/carthageadev/strata/releases/latest/download/Strata.unitypackage)

**أداة Strata لرسم التضاريس والخرائط بأسلوب HD-2D داخل Unity.**

ارسم مستويات الارتفاع والأرضيات والجدران والحواف والسلالم مباشرة في نافذة `Scene`، ثم حوّل النتيجة إلى `Prefab` عادي.

تعمل أدوات الرسم داخل محرّر Unity فقط. تستخدم الخرائط الناتجة مجسّمات `Meshes` وخامات `Materials` عادية، ولا تحتاج إلى أدوات تحرير Strata أثناء تشغيل اللعبة.

## المزايا

- رسم مستويات الارتفاع مع بناء الجدران تلقائيًا عند التقاء مستويين مختلفين.
- طلاء الأرض بطبقات مثل العشب والرمل والرصف، واختيار نمط الجدران لكل جانب.
- إنشاء سلالم وبسطات بين ارتفاعين، بالحجر أو الخشب أو باستخدام مجسّمات درجات مخصّصة.
- تشكيل التلال والوديان والمنحدرات.
- **السحب والإفلات:** أضف الصور والخامات والمجسّمات إلى نافذة Strata، حتى مباشرة من مستعرض الملفات. تتعرّف الأداة على خرائط اللون والسطح والخشونة من أسماء الملفات، وتضبط إعدادات الاستيراد وتجهّز الخامات وخرائط `ORM`.
- تنظيم مجموعات البلاطات والحزم ومشاركتها بصيغة `.unitypackage`.
- استخدام `Pixel Bake` لإعطاء العناصر الزخرفية مظهرًا بأسلوب فن البكسل يناسب التضاريس.
- التراجع عن كل ضربة فرشاة، ونسخ احتياطية عند إجراء `Bake`، واستعادة العمل بعد التعطّل.

## المتطلبات

- محرّر **Unity 6**، بدءًا من `6000.0`.
- خط التصيير **Universal Render Pipeline — URP**. تعتمد الحزمة على الإصدار `17.0.3`.

## التثبيت

### عبر Package Manager

من نافذة `Package Manager`، اختر `+ > Install package from git URL` وألصق الرابط التالي:

<div dir="ltr">

```text
https://github.com/carthageadev/strata.git#unity
```

</div>

يشير الرابط إلى فرع `unity` الذي يحتوي على الأداة.

### التثبيت اليدوي

انسخ مجلد الأداة إلى `Assets/Strata` داخل مشروع Unity، أو استنسخ المستودع داخل مجلد `Assets`.

يمكنك أيضًا تنزيل حزمة `.unitypackage` من [آخر إصدار](https://github.com/carthageadev/strata/releases/latest) واستيرادها إلى مشروعك.

## البداية السريعة

1. أنشئ تضاريس جديدة من `Tools > Strata > New Terrain`.
2. افتح أدوات الرسم من `Tools > Strata > Terrain Editor`، وابدأ العمل في نافذة `Scene`.
3. اختر مجموعات البلاطات من `Tools > Strata > Materials`.
4. استخدم `Bake` لحفظ النتيجة وتحديث المجسّمات ومصادمات `Colliders`.
5. لإعطاء العناصر الزخرفية مظهر البكسل، حدّدها ثم اختر `Tools > Strata > Pixel Bake Selection`.

للتجربة، افتح مشهد `Examples/Strata Sandbox` المرفق مع الأداة.

## التحديثات

تتحقّق Strata من وجود إصدار جديد مرة واحدة في كل جلسة، وتطلب موافقتك قبل التحديث. يمكنك إيقاف التحقّق من `Preferences > Strata > Updates`.

## المساهمة والمساعدة

- للإبلاغ عن مشكلة أو اقتراح ميزة، افتح [Issue](https://github.com/carthageadev/strata/issues/new). اذكر إصدار Unity وخطوات إعادة المشكلة، وأرفق صورة أو رسالة الخطأ إن أمكن.
- للمساهمة في الكود أو تحسين التوثيق والترجمة، افتح [Pull request](https://github.com/carthageadev/strata/pulls) يوضّح التغيير وسبب الحاجة إليه.

</div>
