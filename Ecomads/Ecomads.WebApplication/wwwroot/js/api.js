// /js/api.js

// Р‘Р°Р·РѕРІР°СЏ С„СѓРЅРєС†РёСЏ РґР»СЏ РІС‹РїРѕР»РЅРµРЅРёСЏ Р°РІС‚РѕСЂРёР·РѕРІР°РЅРЅС‹С… Р·Р°РїСЂРѕСЃРѕРІ
export const fetchWithAuth = async (url, options = {}) => {
    const token = localStorage.getItem('ecomads_token');
    
    // Р”РѕР±Р°РІР»СЏРµРј Р·Р°РіРѕР»РѕРІРѕРє Р°РІС‚РѕСЂРёР·Р°С†РёРё Рє Р·Р°РїСЂРѕСЃСѓ
    const authOptions = {
        ...options,
        headers: {
            ...options.headers,
            'Authorization': token ? `Bearer ${token}` : '',
            'Content-Type': 'application/json'
        }
    };
    
    const response = await fetch(url, authOptions);
    
    // Р•СЃР»Рё СЃРµСЂРІРµСЂ РІРµСЂРЅСѓР» 401 Unauthorized, С‚РѕРєРµРЅ РЅРµРґРµР№СЃС‚РІРёС‚РµР»РµРЅ
    if (response.status === 401) {
        //localStorage.removeItem('ecomads_token');
        window.location.href = '/index.html';
        return null;
    }
    
    return response;
};

export const getCampaignsData = async (filters = {}) => {
    try {
        const query = new URLSearchParams();
        if (filters.startDate) query.set('startDate', filters.startDate);
        if (filters.endDate) query.set('endDate', filters.endDate);
        const suffix = query.toString() ? `?${query.toString()}` : '';

        const response = await fetchWithAuth(`/api/projects${suffix}`);
        if (!response || !response.ok) throw new Error('Ошибка сети');
        return await response.json();
    } catch (error) {
        console.error('Ошибка загрузки кампаний:', error);
        return [];
    }
};

export const getLoadedPeriods = async () => {
    try {
        const response = await fetchWithAuth('/api/statistics/periods');
        if (!response || !response.ok) throw new Error('Ошибка загрузки периодов');
        return await response.json();
    } catch (error) {
        console.error('Ошибка загрузки периодов:', error);
        return [];
    }
};

export const getDashboardData = async () => {
    // Р’СЂРµРјРµРЅРЅР°СЏ Р·Р°РіР»СѓС€РєР°, РїРѕРєР° РЅРµ Р±СѓРґРµС‚ РіРѕС‚РѕРІ РєРѕРЅС‚СЂРѕР»Р»РµСЂ РґР»СЏ KPI
    return {
        kpis: {
            spent: "124 560 в‚Ѕ",
            orders: "847",
            drr: "18.5%",
            ctr: "4.2%"
        }
    };
};

// API РґР»СЏ РїРѕР»СѓС‡РµРЅРёСЏ СЃС‚Р°С‚РёСЃС‚РёРєРё СЂРµРєРѕРјРµРЅРґР°С†РёР№
export const getRecommendationsStats = async (period = 'month') => {
    try {
        // РСЃРїРѕР»СЊР·СѓРµРј СЂРµР°Р»СЊРЅС‹Р№ API РґР»СЏ РїРѕР»СѓС‡РµРЅРёСЏ РґР°РЅРЅС‹С…
        const response = await fetchWithAuth(`/api/recommendations/stats?period=${period}`);
        
        if (!response || !response.ok) {
            // Р•СЃР»Рё API РЅРµ СЂР°Р±РѕС‚Р°РµС‚ РёР»Рё РІРµСЂРЅСѓР» РѕС€РёР±РєСѓ, РёСЃРїРѕР»СЊР·СѓРµРј РјРѕРєРѕРІС‹Рµ РґР°РЅРЅС‹Рµ
            console.warn('РќРµ СѓРґР°Р»РѕСЃСЊ РїРѕР»СѓС‡РёС‚СЊ РґР°РЅРЅС‹Рµ СЃ СЃРµСЂРІРµСЂР°, РёСЃРїРѕР»СЊР·СѓРµРј РјРѕРєРѕРІС‹Рµ РґР°РЅРЅС‹Рµ');
            return getMockRecommendationsStats(period);
        }
        
        // РџРѕР»СѓС‡Р°РµРј РґР°РЅРЅС‹Рµ СЃ СЃРµСЂРІРµСЂР°
        const data = await response.json();
        console.log('РџРѕР»СѓС‡РµРЅС‹ РґР°РЅРЅС‹Рµ СЃ СЃРµСЂРІРµСЂР°:', data);
        
        // РџСЂРѕРІРµСЂСЏРµРј СЃС‚СЂСѓРєС‚СѓСЂСѓ РґР°РЅРЅС‹С…
        if (!data) {
            console.error('API РІРµСЂРЅСѓР» РїСѓСЃС‚РѕР№ РѕС‚РІРµС‚');
            return getMockRecommendationsStats(period);
        }
        
        // РљРѕСЂСЂРµРєС‚РёСЂСѓРµРј РґР°РЅРЅС‹Рµ, РµСЃР»Рё С‚СЂРµР±СѓРµС‚СЃСЏ
        const result = {
            counts: data.counts || { accepted: 0, pending: 0, rejected: 0 },
            monthly: data.monthly || [],
            recommendations: []
        };
        
        // РћР±СЂР°Р±Р°С‚С‹РІР°РµРј РјР°СЃСЃРёРІ СЂРµРєРѕРјРµРЅРґР°С†РёР№
        if (data.recommendations && Array.isArray(data.recommendations)) {
            result.recommendations = data.recommendations.map(rec => ({
                id: rec.id || '',
                text: rec.text || '',
                status: rec.status || 'РЅРѕРІР°СЏ',
                date: rec.date || new Date().toISOString(),
                campaign: rec.campaign || 'РќРµ СѓРєР°Р·Р°РЅРѕ',
                comment: rec.comment || ''
            }));
        } else {
            console.warn('API РЅРµ РІРµСЂРЅСѓР» РјР°СЃСЃРёРІ СЂРµРєРѕРјРµРЅРґР°С†РёР№');
        }
        
        console.log('РџСЂРµРѕР±СЂР°Р·РѕРІР°РЅРЅС‹Рµ РґР°РЅРЅС‹Рµ РґР»СЏ UI:', result);
        return result;
    } catch (error) {
        console.error('РћС€РёР±РєР° Р·Р°РіСЂСѓР·РєРё СЃС‚Р°С‚РёСЃС‚РёРєРё СЂРµРєРѕРјРµРЅРґР°С†РёР№:', error);
        // РџСЂРё РѕС€РёР±РєРµ С‚Р°РєР¶Рµ РІРѕР·РІСЂР°С‰Р°РµРј РјРѕРєРѕРІС‹Рµ РґР°РЅРЅС‹Рµ
        console.warn('РР·-Р·Р° РѕС€РёР±РєРё РёСЃРїРѕР»СЊР·СѓРµРј РјРѕРєРѕРІС‹Рµ РґР°РЅРЅС‹Рµ');
        return getMockRecommendationsStats(period);
    }
};

// Р¤СѓРЅРєС†РёСЏ РґР»СЏ РіРµРЅРµСЂР°С†РёРё РјРѕРєРѕРІС‹С… РґР°РЅРЅС‹С…
function getMockRecommendationsStats(period) {
    // РќР°СЃС‚СЂР°РёРІР°РµРј РєРѕР»РёС‡РµСЃС‚РІРѕ Р·Р°РїРёСЃРµР№ РІ Р·Р°РІРёСЃРёРјРѕСЃС‚Рё РѕС‚ РІС‹Р±СЂР°РЅРЅРѕРіРѕ РїРµСЂРёРѕРґР°
    let counts = { accepted: 0, pending: 0, rejected: 0 };
    let monthly = [];
    let recommendations = [];
    
    // Р“РµРЅРµСЂРёСЂСѓРµРј СЂР°Р·РЅС‹Рµ РґР°РЅРЅС‹Рµ РІ Р·Р°РІРёСЃРёРјРѕСЃС‚Рё РѕС‚ РїРµСЂРёРѕРґР°
    switch(period) {
        case 'week':
            counts = { accepted: 8, pending: 3, rejected: 2 };
            monthly = generateMonthlyData(3); // РџРѕСЃР»РµРґРЅРёРµ 3 РјРµСЃСЏС†Р°
            recommendations = generateMockRecommendations(5);
            break;
        case 'month':
            counts = { accepted: 24, pending: 11, rejected: 7 };
            monthly = generateMonthlyData(6); // РџРѕСЃР»РµРґРЅРёРµ 6 РјРµСЃСЏС†РµРІ
            recommendations = generateMockRecommendations(10);
            break;
        case 'quarter':
            counts = { accepted: 57, pending: 23, rejected: 18 };
            monthly = generateMonthlyData(6); // РџРѕСЃР»РµРґРЅРёРµ 6 РјРµСЃСЏС†РµРІ
            recommendations = generateMockRecommendations(15);
            break;
        case 'year':
            counts = { accepted: 142, pending: 56, rejected: 34 };
            monthly = generateMonthlyData(12); // Р’СЃРµ 12 РјРµСЃСЏС†РµРІ
            recommendations = generateMockRecommendations(20);
            break;
    }
    
    return { counts, monthly, recommendations };
}

// Р“РµРЅРµСЂР°С†РёСЏ РґР°РЅРЅС‹С… РїРѕ РјРµСЃСЏС†Р°Рј
function generateMonthlyData(monthCount) {
    const months = ['РЇРЅРІ', 'Р¤РµРІ', 'РњР°СЂС‚', 'РђРїСЂ', 'РњР°Р№', 'РСЋРЅСЊ', 'РСЋР»СЊ', 'РђРІРі', 'РЎРµРЅ', 'РћРєС‚', 'РќРѕСЏ', 'Р”РµРє'];
    const currentMonth = new Date().getMonth();
    const result = [];
    
    for (let i = 0; i < monthCount; i++) {
        const monthIndex = (currentMonth - monthCount + i + 12) % 12; // РџРѕР»СѓС‡Р°РµРј РїСЂР°РІРёР»СЊРЅС‹Р№ РёРЅРґРµРєСЃ РјРµСЃСЏС†Р°
        
        // Р“РµРЅРµСЂРёСЂСѓРµРј СЃР»СѓС‡Р°Р№РЅС‹Рµ РґР°РЅРЅС‹Рµ
        const accepted = Math.floor(Math.random() * 20) + 5;
        const pending = Math.floor(Math.random() * 10) + 1;
        const rejected = Math.floor(Math.random() * 8) + 1;
        const total = accepted + pending + rejected;
        
        result.push({
            month: months[monthIndex],
            accepted,
            pending,
            rejected,
            total
        });
    }
    
    return result;
}

// Р“РµРЅРµСЂР°С†РёСЏ РїСЂРёРјРµСЂРѕРІ СЂРµРєРѕРјРµРЅРґР°С†РёР№
function generateMockRecommendations(count) {
    const recommendations = [
        {
            text: 'РЈРІРµР»РёС‡РёС‚СЊ СЃС‚Р°РІРєРё РґР»СЏ РєР»СЋС‡РµРІРѕРіРѕ СЃР»РѕРІР° "РЎРјР°СЂС‚С„РѕРЅ Samsung" РЅР° 15%',
            campaign: 'Р­Р»РµРєС‚СЂРѕРЅРёРєР° 2023',
            status: 'accepted',
            comment: 'РЎС‚Р°РІРєРё РїРѕРІС‹С€РµРЅС‹, РЅР°Р±Р»СЋРґР°РµС‚СЃСЏ СЂРѕСЃС‚ РїРѕРєР°Р·РѕРІ'
        },
        {
            text: 'Р”РѕР±Р°РІРёС‚СЊ РјРёРЅСѓСЃ-СЃР»РѕРІР° РґР»СЏ РєР°РјРїР°РЅРёРё "Р”РµС‚СЃРєРёРµ С‚РѕРІР°СЂС‹"',
            campaign: 'Р”РµС‚СЃРєРёРµ С‚РѕРІР°СЂС‹',
            status: 'pending',
            comment: 'РќРµРѕР±С…РѕРґРёРјРѕ СѓС‚РѕС‡РЅРёС‚СЊ СЃРїРёСЃРѕРє РјРёРЅСѓСЃ-СЃР»РѕРІ'
        },
        {
            text: 'РћСЃС‚Р°РЅРѕРІРёС‚СЊ РїРѕРєР°Р·С‹ РґР»СЏ РїР»РѕС‰Р°РґРєРё social.example.com',
            campaign: 'РћР±С‰Р°СЏ РєР°РјРїР°РЅРёСЏ',
            status: 'rejected',
            comment: 'РџР»РѕС‰Р°РґРєР° РїСЂРёРЅРѕСЃРёС‚ РєРѕРЅРІРµСЂСЃРёРё, РѕСЃС‚Р°РЅРѕРІРєР° РЅРµС†РµР»РµСЃРѕРѕР±СЂР°Р·РЅР°'
        },
        {
            text: 'РЈРІРµР»РёС‡РёС‚СЊ Р±СЋРґР¶РµС‚ РєР°РјРїР°РЅРёРё "Р—РёРјРЅСЏСЏ РѕР±СѓРІСЊ" РЅР° 30%',
            campaign: 'Р—РёРјРЅСЏСЏ РѕР±СѓРІСЊ',
            status: 'accepted',
            comment: 'Р‘СЋРґР¶РµС‚ СѓРІРµР»РёС‡РµРЅ'
        },
        {
            text: 'РР·РјРµРЅРёС‚СЊ С‚Р°СЂРіРµС‚РёРЅРі РєР°РјРїР°РЅРёРё РЅР° РІРѕР·СЂР°СЃС‚РЅСѓСЋ РіСЂСѓРїРїСѓ 25-44',
            campaign: 'РђРєСЃРµСЃСЃСѓР°СЂС‹',
            status: 'pending',
            comment: ''
        },
        {
            text: 'Р”РѕР±Р°РІРёС‚СЊ РѕР±СЉСЏРІР»РµРЅРёСЏ РґР»СЏ РЅРѕРІРѕР№ Р»РёРЅРµР№РєРё С‚РѕРІР°СЂРѕРІ',
            campaign: 'РќРѕРІРёРЅРєРё',
            status: 'accepted',
            comment: 'РћР±СЉСЏРІР»РµРЅРёСЏ РґРѕР±Р°РІР»РµРЅС‹'
        },
        {
            text: 'РћРїС‚РёРјРёР·РёСЂРѕРІР°С‚СЊ РјРѕР±РёР»СЊРЅС‹Рµ СЃС‚Р°РІРєРё (-20%)',
            campaign: 'Р­Р»РµРєС‚СЂРѕРЅРёРєР° 2023',
            status: 'rejected',
            comment: 'Р‘РѕР»СЊС€Р°СЏ С‡Р°СЃС‚СЊ С‚СЂР°С„РёРєР° РїСЂРёС…РѕРґРёС‚ СЃ РјРѕР±РёР»СЊРЅС‹С… СѓСЃС‚СЂРѕР№СЃС‚РІ'
        },
        {
            text: 'Р”РѕР±Р°РІРёС‚СЊ Р±С‹СЃС‚СЂС‹Рµ СЃСЃС‹Р»РєРё РІ РѕР±СЉСЏРІР»РµРЅРёСЏ',
            campaign: 'Р‘С‹С‚РѕРІР°СЏ С‚РµС…РЅРёРєР°',
            status: 'accepted',
            comment: 'РЎСЃС‹Р»РєРё РґРѕР±Р°РІР»РµРЅС‹'
        },
        {
            text: 'РСЃРєР»СЋС‡РёС‚СЊ РЅРµС†РµР»РµРІС‹Рµ СЂРµРіРёРѕРЅС‹ РёР· С‚Р°СЂРіРµС‚РёРЅРіР°',
            campaign: 'Р РµРіРёРѕРЅР°Р»СЊРЅР°СЏ',
            status: 'pending',
            comment: 'Р’ РїСЂРѕС†РµСЃСЃРµ Р°РЅР°Р»РёР·Р° СЂРµРіРёРѕРЅРѕРІ'
        },
        {
            text: 'РЎРѕР·РґР°С‚СЊ РѕС‚РґРµР»СЊРЅСѓСЋ РєР°РјРїР°РЅРёСЋ РґР»СЏ Р±СЂРµРЅРґРѕРІРѕРіРѕ РїРѕРёСЃРєР°',
            campaign: 'РћР±С‰Р°СЏ РєР°РјРїР°РЅРёСЏ',
            status: 'accepted',
            comment: ''
        },
        {
            text: 'РЈРІРµР»РёС‡РёС‚СЊ СЃС‚Р°РІРєРё РІ РїСЂР°Р№Рј-С‚Р°Р№Рј РЅР° 25%',
            campaign: 'РђРєС‚РёРІРЅР°СЏ',
            status: 'rejected',
            comment: 'РќРµРґРѕСЃС‚Р°С‚РѕС‡РЅС‹Р№ Р±СЋРґР¶РµС‚'
        }
    ];
    
    // РџРµСЂРµРјРµС€РёРІР°РµРј СЂРµРєРѕРјРµРЅРґР°С†РёРё
    const shuffled = [...recommendations].sort(() => 0.5 - Math.random());
    
    // Р‘РµСЂРµРј РЅСѓР¶РЅРѕРµ РєРѕР»РёС‡РµСЃС‚РІРѕ Рё РґРѕР±Р°РІР»СЏРµРј РґР°С‚С‹ (СЃР»СѓС‡Р°Р№РЅС‹Рµ РІ РїСЂРµРґРµР»Р°С… РїРѕСЃР»РµРґРЅРёС… 30 РґРЅРµР№)
    return shuffled.slice(0, count).map(rec => {
        const daysAgo = Math.floor(Math.random() * 30) + 1;
        const date = new Date();
        date.setDate(date.getDate() - daysAgo);
        
        return {
            ...rec,
            date: date.toISOString()
        };
    }).sort((a, b) => new Date(b.date) - new Date(a.date)); // РЎРѕСЂС‚РёСЂСѓРµРј РїРѕ РґР°С‚Рµ (РЅРѕРІС‹Рµ СЃРІРµСЂС…Сѓ)
}

